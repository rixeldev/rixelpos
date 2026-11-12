# Arquitectura de RixelPOS

## 1. Principios

1. **Offline-first y transaccional.** La caja debe seguir vendiendo sin red y una venta
   debe ser atómica. SQLite en modo WAL y una transacción por venta son la base.
2. **Dinero exacto.** Todo importe se guarda en **centavos enteros** (`long`). No se usa
   `float` ni `double` para dinero. Las cantidades de inventario se guardan en milésimas
   enteras (`stock_milli`).
3. **Núcleo independiente de la UI y del hardware.** Dominio y aplicación no referencian
   Avalonia ni `System.IO.Ports`.
4. **Adaptadores reemplazables.** Persistencia, impresora, escáner, cajón, PDF, fiscal y
   respaldos están detrás de interfaces. Los tests usan mocks o implementaciones reales
   locales (por ejemplo, transporte `file:`).

## 2. Capas y dependencias

```
Desktop (Avalonia) ──> Application ──> Domain
       │                    │
       └──────────> Infrastructure ──> Application/Domain
```

- `RixelPOS.Domain`: entidades, `Money`, `Discount`, `SaleCalculator`, `InvoiceNumberFormatter`,
  `BusinessSettings`, reportes como DTO e interfaces de permisos. Sin I/O.
- `RixelPOS.Application`: `IPosStore`, casos de uso (`CheckoutService`, `InventoryService`,
  `CashRegisterService`, `ReportService`, `SetupService`, `AuthService`), DTO de impresión,
  contratos de hardware y fiscal.
- `RixelPOS.Infrastructure`: implementación SQLite/Dapper, migraciones, bcrypt, logger de
  archivo, ESC/POS, PDF, backup, descubrimiento de dispositivos, escáner serial.
- `RixelPOS.Desktop`: MVVM con CommunityToolkit, vistas Avalonia, i18n, tema, diálogos,
  orquestación de ventanas (setup → login → main) y atajos.

## 3. Modelo de datos

SQLite con `PRAGMA journal_mode=WAL`, `foreign_keys=ON`, `busy_timeout=5000` y
`sequential=NORMAL`. Migraciones embebidas en
`src/RixelPOS.Infrastructure/Data/Migrations/` (aditivas: `001_initial.sql`,
`002_tax_inclusive_pricing.sql`, `003_zero_default_tax.sql`, …) aplicadas por
`SqliteDb.Initialize()` y registradas en `schema_migrations`.

Tablas y relaciones principales:

| Tabla | Propósito |
| --- | --- |
| `settings` | clave/valor de configuración del negocio |
| `sequences` | consecutivos atómicos (`sale`, `invoice`, `purchase`, `stock_count`, `internal_barcode`) |
| `users` | usuarios, rol, hashes bcrypt de PIN/contraseña |
| `categories`, `tax_rates` | catálogo |
| `products` | producto, precio/costo en centavos, stock en milésimas, impuesto |
| `product_barcodes` | múltiples códigos por producto (único global) |
| `customers`, `suppliers` | terceros |
| `sales`, `sale_lines`, `payments` | venta, líneas congeladas (nombre, precio, costo, impuesto), pagos |
| `invoices` | factura, consecutivo, prefijo, datos fiscales JSON, estado |
| `cash_sessions`, `cash_movements` | turnos y movimientos de caja |
| `stock_movements` | bitácora de entradas/salidas/ajustes/conteo |
| `stock_counts`, `stock_count_lines` | conteo físico y diferencias |
| `purchase_orders`, `purchase_order_lines` | compras y recepción |
| `audit_log` | auditoría de acciones sensibles |

Índices relevantes: `product_barcodes.code` (único), `products.sku` (único),
`sales.completed_at`, `sales.session_id`, `sale_lines.sale_id`, `payments.sale_id`,
`invoices.number` (único), `stock_movements.product_id`, `audit_log.created_at`.

Los campos de fecha se guardan como ISO-8601 UTC y se convierten a hora local solo en la UI.

## 4. Matemática de venta

`SaleCalculator.Calculate` es puro y determinista:

1. `gross = round(quantity × unitPriceCents)`.
2. Descuento de línea (porcentaje en puntos base o monto fijo) acotado al bruto.
3. Descuento de venta: se calcula sobre la suma de los brutos después del descuento de línea
   y se reparte proporcionalmente con **mayor residuo**; la suma de asignaciones es exacta.
4. Por cada línea:
   - impuesto **exclusivo**: `tax = round(net × rate / 10000)`;
   - impuesto **inclusivo**: `net = round(gross × 10000 / (10000 + rate))`, `tax = gross - net`.
5. Se suman neto, impuesto y total. Todo redondeo usa `MidpointRounding.AwayFromZero`.

Por defecto **el precio de venta ya incluye impuesto** y **la tasa por defecto es 0%**:
se guarda el precio final que paga el cliente y, si hay tasa, el impuesto se extrae del
mismo (`net = gross - tax`). Las migraciones `002` y `003` marcan los productos existentes
como inclusivos y eliminan la tasa implícita del 18% para que el total sea el precio
registrado. Los tests cubren impuestos 18%, exentos, descuentos y reparto de residuos.

## 5. Flujo transaccional de una venta

`CheckoutService.CompleteSale`:

1. Exige usuario autenticado y turno de caja abierto.
2. Valida carrito, pagos, vueltos, crédito y stock.
3. Abre una transacción SQLite (`IPosStore.ExecuteInTransaction`).
4. Relee productos y cliente dentro de la transacción (evita carreras).
5. Asigna `sale.Number` y el consecutivo de factura desde `sequences`
   (`INSERT … ON CONFLICT … RETURNING`).
6. Genera datos fiscales con el proveedor configurado (no-op o NCF).
7. Inserta venta, líneas, pagos y factura.
8. Descuenta stock y registra `stock_movements`.
9. Ajusta balance/crédito a favor del cliente.
10. Escribe auditoría.
11. Hace commit. Recién después de un commit exitoso se imprime el recibo.

Si algo falla (stock insuficiente, DB ocupada, corte de energía), la transacción revierte y
el carrito permanece intacto. El modo WAL de SQLite evita corrupción ante apagones.

## 6. Consecutivos y facturas

`sequences` guarda un contador por nombre. El número se obtiene con una sola sentencia
atómica. La factura usa `InvoiceNumberFormatter.Format(prefix, sequence, padding)` y se
guarda en `invoices` con índice único. Las ventas en espera no consumen consecutivo.

## 7. Impuestos y capa fiscal

- `TaxRate` es configuración; cada producto lleva su `tax_rate_bp` y `tax_inclusive`.
- `IFiscalProvider` recibe (settings, sale, customer, sequence) y devuelve número fiscal,
  JSON y líneas extra de recibo.
- `NoFiscalProvider` no hace nada.
- `DominicanRepublicNcfProvider` genera NCF `B02…` y guarda RNC/JSON. Está desacoplado para
  añadir e-CF, firma y envío DGII sin tocar el checkout.

## 8. Hardware

`IReceiptPrinter`, `ILabelPrinter`, `IScannerListener`, `ICustomerDisplay`,
`IDeviceDiscovery` viven en `Application.Printing`. Las implementaciones ESC/POS están en
`Infrastructure.Hardware`:

- `EscPosBuilder`: comandos, texto ASCII, QR raster (QRCoder), logo raster (SkiaSharp),
  Code128, EAN-13, corte y pulso de cajón.
- Transportes: TCP 9100, serial, CUPS (`lp -o raw`), dispositivo `/dev/usb/lp*`,
  spooler raw de Windows (`winspool.drv`), archivo para tests.
- `EscPosPrinter` convierte errores técnicos en mensajes humanos y códigos estables.

La UI de checkout usa el escáner como teclado: el foco vive en el buscador y Enter agrega.
El escáner serial usa `SerialScannerListener` y se enruta al `ScanRouter`; la pantalla activa
decide si es venta, recepción o conteo.

## 9. Persistencia y transacciones anidadas

`SqliteStore` usa `AsyncLocal<DbSession>` para que las llamadas anidadas (por ejemplo,
auditoría dentro de una transacción de venta) participen de la misma conexión y transacción.
No hay conexiones globales compartidas entre hilos. Las lecturas abren conexión corta.

## 10. Respaldos y restauración

- `VACUUM INTO` crea una copia consistente aunque la DB esté en uso.
- Nombres `rixelpos-backup-YYYYMMDD-HHmmss.db`.
- Retención configurable; se purgan los más antiguos.
- Restauración: checkpoint, `ClearAllPools`, copia sobre `rixelpos.db`, y aviso de reinicio.
  Antes de restaurar se crea un respaldo de seguridad.

## 11. Logging

`FileLogger` escribe JSON-lines en `logs/YYYY-MM-DD.log`, rota a 5 MB y nunca lanza
excepciones que interrumpan una venta. Los errores de hardware y de impresión quedan
registrados con contexto.

## 12. Sincronización futura (diseño)

1. Añadir un `outbox` (`sync_outbox`) escrito en la misma transacción que la venta.
2. Un terminal principal actúa como servidor LAN (Kestrel local, autenticación por token
   de terminal) y aplica eventos idempotentes (`event_id`).
3. Las cajas secundarias sincronizan ventas, movimientos y catálogo; los conflictos de
   stock se resuelven por movimiento, no por valor absoluto.
4. Un conector opcional sube el outbox a la nube. La caja sigue funcionando offline y la
   UI no cambia.

## 13. Decisiones prácticas

- Se eligió Dapper en lugar de un ORM completo para controlar SQL y transacciones.
- El parámetro casing de Microsoft.Data.Sqlite es sensible; `SqliteExtensions` normaliza los
  placeholders antes de enlazar para evitar errores silenciosos.
- Los `DataGrid` de Avalonia son de solo lectura; las acciones se hacen con botones.
- El idioma se resuelve con un markup extension que devuelve texto en tiempo de carga. El
  cambio de idioma pide reinicio para evitar bindings complejos y frágiles.
