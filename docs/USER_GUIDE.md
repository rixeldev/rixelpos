# Guía de uso de RixelPOS

## 1. Primer arranque

1. Abre RixelPOS desde el menú de aplicaciones o el acceso directo.
2. Completa el asistente:
   - **Negocio**: nombre, razón social, RNC, teléfono, dirección.
   - **Moneda e impuestos**: moneda, símbolo, nombre del impuesto (ITBIS), tasa, precios
     con impuesto incluido o no, prefijo de factura.
   - **Impresora**: detecta la impresora o escribe la conexión. Puedes omitirlo.
   - **Administrador**: nombre, usuario, contraseña y PIN.
3. Inicia sesión con el PIN del administrador.
4. Sigue el recorrido de 3 pasos (se puede saltar).

## 2. Iniciar sesión

- **PIN**: escribe 4–8 dígitos y pulsa `→` o `Enter`.
- **Usuario y contraseña**: enlace “Entrar con usuario y contraseña”.
- El PIN también se usa para que un encargado apruebe descuentos, anulaciones y
  devoluciones.

## 3. Abrir la caja

Menú **Caja → Abrir caja**. Ingresa el fondo inicial (puede ser 0).
Sin turno abierto no se puede cobrar. El estado se ve en la cabecera (“Turno abierto” /
“Caja cerrada”).

## 4. Hacer una venta

1. `F2` o menú **Vender**.
2. Escanea un código o escribe nombre/SKU. Presiona `Enter`.
3. El producto aparece en el carrito. Escanear de nuevo aumenta la cantidad.
4. Ajusta:
   - `＋` / `−`: cantidad.
   - Número de cantidad: toca el número para escribir otra cantidad.
   - Etiqueta: descuento por línea (% o monto).
   - Papelera: quitar.
   - **Descuento**: descuento sobre toda la venta.
5. Opcional: **Consumidor final** → elegir/crear cliente. Un cliente es obligatorio para
   crédito o crédito a favor.
6. **En espera** guarda el carrito para atender a otro cliente.
7. **Cobrar** o `Enter` con el buscador vacío.

### Cobrar
- Elige **Efectivo**, **Tarjeta**, **Transferencia**, **Crédito a favor** o **Crédito**.
- Escribe el monto o deja vacío para usar el saldo pendiente.
- En efectivo, el campo es el dinero recibido; el cambio se calcula solo.
- Puedes agregar varios pagos (pago mixto). “Falta por pagar” debe llegar a 0.
- **Confirmar cobro**. El recibo se imprime si está configurado.
- En la ventana final puedes reimprimir o guardar la factura PDF.

### Anular una venta
En **Inicio → Últimas facturas**, botón `×` de la venta. Se pide motivo y, según el rol,
PIN de encargado. El stock se restaura y la factura queda anulada.

## 5. Ventas en espera

**En espera** guarda el carrito con una etiqueta. El botón ▶ en el carrito abre la lista;
selecciona y **Retomar venta**. También puedes eliminar una venta en espera desde la lista.

## 6. Recibos y facturas

- Recibo térmico automático si `Ajustes → Recibos → Imprimir automáticamente` está activo.
- **Imprimir recibo** en la ventana de venta completada.
- **Guardar factura PDF** en `Documentos/RixelPOS` (Linux: `~/Documents/RixelPOS`).
- Reimpresión desde el inicio o desde el historial del cliente.
- **Reportes → Reimprimir** busca por número (`A-000001`) y permite reimprimir o exportar PDF.

## 7. Inventario

### Productos
- **Inventario → Productos → Nuevo producto**.
- Obligatorio: nombre. Recomendado: precio y existencia inicial.
- Puedes agregar varios códigos de barras, generar uno interno, categoría, costo,
  impuesto, favorito y stock mínimo.
- El botón ✎ edita; `±` ajusta existencia con motivo; `▤` imprime etiqueta; `×` archiva.

### Recibir mercancía por escaneo
1. **Inventario → Recibir mercancía**.
2. Escanea. Si el código existe, se suma 1 a la línea (o crea una).
3. Si no existe, se abre el formulario con el código precargado; guarda nombre y precio.
4. Revisa cantidades y **Confirmar recepción**. Todo se guarda en una sola transacción.
5. La pantalla muestra creados, actualizados y unidades recibidas.

### Conteo físico
1. **Inventario → Conteo físico → Iniciar conteo**.
2. Escanea cada producto de la estantería; se incrementa lo contado.
3. La tabla muestra sistema, contado y diferencia.
4. **Finalizar conteo** con “Aplicar ajustes al inventario” activo.
5. Puedes exportar el resumen en CSV.

### CSV
- **Exportar CSV** genera un archivo compatible con Excel/LibreOffice.
- **Descargar plantilla** incluye dos filas de ejemplo.
- **Importar CSV** actualiza por SKU o código de barras si existe; si no, crea.

## 8. Clientes

- Buscar por nombre/teléfono/RNC.
- **Nuevo cliente**: nombre obligatorio; teléfono, RNC, dirección, correo, límite de crédito.
- Detalle: balance, crédito a favor e historial.
- **Cargar crédito**: suma saldo a favor (se registra como entrada de caja si hay turno).
- **Abonar balance**: registra el pago de una deuda.
- Las ventas a crédito y el crédito a favor se activan en Ajustes → Impuestos.

## 9. Proveedores y compras

- **Proveedores → Nuevo proveedor**.
- **Órdenes de compra → Nueva orden**: elige proveedor, agrega productos con cantidad y
  costo. Al crear, se genera `OC-000001`.
- **Recibir mercancía**: suma stock y actualiza el último costo del producto. La orden
  queda “Recibida”.

## 10. Caja y cierre del día

- **Caja** muestra fondo inicial, efectivo esperado, ventas del turno y desglose por pago.
- **Entrada/Salida de efectivo** con motivo (propinas, retiros, cambio extra).
- **Cerrar caja**: cuenta el efectivo físico, ingresa el monto y confirma.
  - Se muestra el **Reporte Z** con abierto/cerrado, esperado/contado, diferencia y totales.
- El historial permite consultar el reporte Z de turnos anteriores.

## 11. Reportes

`F6` o menú **Reportes**:
- Hoy, últimos 7 días, este mes o rango personalizado.
- Ventas brutas, cantidad, impuestos, ganancia y ticket promedio.
- Ventas por día (barras), productos más vendidos, pagos, cajeros y categorías.
- **Exportar CSV** / **Exportar PDF**.
- **Reimprimir** por número de factura.

## 12. Ajustes

- **Negocio**: datos del recibo/factura y logo.
- **Impuestos**: moneda, ITBIS, prefijo, consecutivo, NCF opcional, crédito, stock negativo,
  umbral de descuento y aprobaciones.
- **Recibos**: impresora, ancho, pie, QR, autoimpresión, cajón, escáner, pantalla cliente.
- **Usuarios**: crear/editar roles, PIN, contraseña, activar/desactivar.
- **Respaldos**: carpeta, retención, crear y restaurar.
- **Apariencia**: tema claro/oscuro/sistema, idioma (se aplica al reiniciar), escala.
- **Auditoría**: últimas 300 acciones.
- **Acerca de**: versión, soporte y rutas de datos/logs.

## 13. Respaldos y restauración

- La base local está en `%APPDATA%\rixelpos` (Windows) o `~/.local/share/rixelpos` (Linux).
- **Crear respaldo ahora** genera `rixelpos-backup-AAAAMMDD-HHMMSS.db`.
- **Restaurar** reemplaza los datos actuales; antes crea un respaldo de seguridad y pide
  reiniciar RixelPOS.
- Copia la carpeta de respaldos a un disco externo o a la nube periódicamente.

## 14. Atajos

| Tecla | Acción |
| --- | --- |
| `F1` | Inicio |
| `F2` | Vender |
| `F3` | Inventario |
| `F4` | Clientes |
| `F5` | Caja |
| `F6` | Reportes |
| `F7` | Ajustes |
| `F8` | Proveedores |
| `Enter` | Agregar producto / abrir cobro / confirmar |
| `Esc` | Cerrar búsqueda o diálogo |
| `Ctrl+L` | Cerrar sesión |

## 15. Problemas frecuentes

- **No cobra**: no hay turno abierto. Abre la caja.
- **No imprime**: revisa Ajustes → Recibos; usa “Probar impresora”. En Linux, permisos `lp`.
- **Escáner no agrega**: haz clic en el buscador. En recepción, vuelve a la pestaña.
- **No aparecen productos**: importa CSV o crea uno; el buscador vacío muestra favoritos.
- **Error de stock**: el producto no tiene existencias suficientes; recibe mercancía o
  ajusta. Se puede permitir stock negativo en Ajustes.
- **No recuerdo el PIN**: un administrador puede cambiarlo en Ajustes → Usuarios.
- **No hay internet**: RixelPOS no lo necesita. La impresora de red sí requiere LAN.
