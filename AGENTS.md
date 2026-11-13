# AGENTS.md — RixelPOS

> Instrucciones normativas para cualquier agente (humano o IA) que trabaje en este
> repositorio. **Mantén este archivo actualizado cuando cambie la arquitectura, el
> empaquetado, el flujo de releases o las reglas de UI.** Es la fuente de verdad para
> no romper el producto ni perder contexto entre sesiones.

## 1. Identidad del proyecto

- Producto: **RixelPOS** (`RixelDev`).
- Tipo: aplicación de escritorio **nativa**, offline-first, para punto de venta y
  facturación de pequeños/medianos negocios.
- Plataformas objetivo: **Linux (CachyOS / Arch, AppImage/deb/pacman)** y
  **Windows 10/11 (instalador y portable)**.
- Stack: **.NET 8 (LTS), C# 12, Avalonia UI 11.3**, CommunityToolkit.Mvvm,
  Microsoft.Data.Sqlite + Dapper, BCrypt, SkiaSharp, QRCoder.
- Repositorio remoto: `git@github.com:rixeldev/rixelpos.git` (privado).
- Estilo de respuesta del agente: **español** por defecto, salvo que el usuario pida
  otro idioma.

## 2. Reglas de oro (no negociables)

1. **Aplicación de escritorio nativa.** Prohibido Electron, Tauri, webview, PWA,
   servidor web local que el usuario abra en un navegador o UI basada en HTML.
2. **Offline-first.** La caja debe funcionar sin internet. La app self-contained no
   requiere .NET instalado; el bootstrap de .NET es solo una ayuda opcional para
   desarrollo/actualizaciones.
3. **Dinero en centavos enteros (`long`), nunca `float`/`double`.** Cantidades de
   stock en milésimas enteras (`stock_milli`). Redondeo `AwayFromZero`.
   **Los precios de venta ya incluyen impuesto**: el precio que escribe el usuario es el
   total que paga el cliente y el impuesto se extrae del mismo. No añadas el impuesto
   encima del total; `PricesIncludeTax` y `Product.TaxInclusive` son `true` por defecto.
   **La tasa por defecto es 0%**: no asumas ITBIS/IVA. Solo se aplica si el negocio la
   configura o el producto la define.
4. **Toda venta es atómica** dentro de una transacción SQLite. La numeración de factura,
   stock, pagos, crédito y auditoría se guardan en la misma transacción.
5. **Migraciones aditivas.** Nunca edites una migración ya publicada; crea
   `002_*.sql`, `003_*.sql`, etc. La app aplica migraciones al iniciar.
6. **Nada de secretos** en el repositorio: ni bases reales, logs, backups, certificados,
   tokens ni contraseñas. Usa variables de entorno/secretos de CI.
7. **No borres datos del usuario.** Los datos viven en:
   - Linux: `~/.local/share/rixelpos/`
   - Windows: `%APPDATA%\rixelpos\`
   Usa `RIXELPOS_DATA_DIR` para pruebas y nunca apuntes pruebas a la carpeta real.
8. **Todo cambio de UI debe cumplir la sección 7 (UI/UX).**
9. Antes de commit: `dotnet build RixelPOS.sln -c Release` y
   `dotnet test RixelPOS.sln -c Release` deben pasar.
10. **Push autónomo autorizado (política permanente).** Cuando acumules varios cambios
    importantes: sube `<Version>` en `Directory.Build.props`, actualiza docs/AGENTS,
    verifica build + tests, haz commit y `git push origin main`; después crea y sube el tag
    `vX.Y.Z`. No publiques desde un fork ni reescribas releases ya publicadas.

## 3. Comandos esenciales

```bash
dotnet restore RixelPOS.sln
dotnet build RixelPOS.sln -c Release
dotnet test RixelPOS.sln -c Release
dotnet run --project src/RixelPOS.Desktop

# Entorno aislado de desarrollo
RIXELPOS_DATA_DIR=/tmp/rixelpos-dev dotnet run --project src/RixelPOS.Desktop

# Linux packaging (tar, AppImage si hay appimagetool, deb si hay dpkg-deb, pacman)
./packaging/linux/build.sh 1.0.0

# Windows packaging (requiere Inno Setup para el .exe)
powershell -ExecutionPolicy Bypass -File packaging/windows/build.ps1 -Version 1.0.0

# Bootstrap opcional de .NET SDK
./packaging/prerequisites/install-dotnet.sh 8.0
powershell -ExecutionPolicy Bypass -File packaging/prerequisites/install-dotnet.ps1 -Channel 8.0
```

## 4. Estructura y dónde tocar

```
src/RixelPOS.Domain/          Entidades, Money, Discount, SaleCalculator, Branding.
src/RixelPOS.Application/     Casos de uso, IPosStore, hardware/fiscal abstractions.
src/RixelPOS.Infrastructure/  SQLite/Dapper, migraciones, ESC/POS, PDF, bcrypt,
                              backups, logging, hardware real.
src/RixelPOS.Desktop/         Avalonia UI, MVVM, i18n, tema, diálogos, WindowSizing.
tests/                        xUnit de dominio e integración.
packaging/linux/              build.sh, install.sh, PKGBUILD, make-repo.py, udev.
packaging/windows/            build.ps1 + installer.iss.
packaging/prerequisites/      instaladores opcionales de .NET SDK.
docs/                         arquitectura, hardware, guía de usuario, releases, capturas.
                              Releases/pacman: docs/RELEASES.md.
.github/workflows/            CI y release automática.
```

Cambios típicos:
- **Regla de negocio/dinero/impuestos** → `Domain/Services/SaleCalculator.cs` + tests.
- **Caso de uso** → `Application/<área>` + `IPosStore` si necesita persistencia.
- **SQL/tabla/migración** → `Infrastructure/Data` + `docs/ARCHITECTURE.md`.
- **Hardware** → `Infrastructure/Hardware` detrás de las interfaces de
  `Application/Printing/Contracts.cs`; incluye mock/file transport.
- **Pantalla** → `Desktop/Views/Pages|Dialogs` + ViewModel; textos en ambos JSON.
- **Marca** → `Domain/Common/Branding.cs` (nombre, empresa, moneda por defecto).

## 5. Flujo de datos y calidad

- `IPosStore.ExecuteInTransaction` usa `AsyncLocal` para que llamadas anidadas
  (auditoría, stock, crédito) compartan la misma conexión/transacción.
- Las lecturas abren conexión corta; no hay conexión global compartida.
- `SqliteExtensions` normaliza el casing de parámetros; Microsoft.Data.Sqlite es
  sensible a mayúsculas. Mantén los placeholders SQL en camelCase.
- Fechas en DB: ISO-8601 UTC; UI en hora local.
- El reporte de ganancia usa el costo congelado en `sale_lines.cost_cents`.
- Logs JSON-lines en `logs/` con rotación; nunca lanzar excepción de logging durante
  una venta.
- Errores de negocio = `BusinessRuleException` con mensaje humano; UI muestra mensaje,
  no stack trace.

## 6. Localización

- Español e inglés obligatorios: `Localization/es.json` y `en.json`.
- Añade **la misma clave a ambos**; no uses texto visible hardcodeado en XAML.
- En XAML: `{loc:Tr clave}`. En código: `LocalizationService.Instance.Get("clave")`.
- El cambio de idioma se aplica al reiniciar; no lo cambies a bindings frágiles sin
  probar toda la app.
- Mantén los comprobantes impresos en español por defecto; si se añade i18n de recibo,
  hazlo en `ReceiptFormatter` y documenta.

## 7. UI/UX: reglas obligatorias

- **DPI**: toda ventana debe caber en `Screen.WorkingArea / Screen.Scaling`.
  Usa `WindowSizing.ClampOnOpened(this)` en ventanas nuevas (o deriva de `DialogWindow`).
- **Resize**: nada de contenido cortado. Usa `ScrollViewer` para contenido largo,
  `MinWidth/MinHeight` razonables, grids con `*` y tablas con scroll horizontal.
- **Centrado**: botones/iconos centrados horizontal y verticalmente. Usa
  `VerticalContentAlignment="Center"` y anchos consistentes para grupos de acciones.
- **Sin duplicados**: el título de sección se muestra una vez en la cabecera de la app;
  no repitas “Ajustes”, “Pago”, “Consumidor final”, etc.
- **Toque y teclado**: botones ≥40 px, acciones principales ≥54 px; `Enter` agrega/cobra,
  `Esc` cierra, `F1–F8` navega, `Ctrl+L` cierra sesión. Al escanear o escribir un código
  exacto en el checkout, el artículo se agrega solo (sin pulsar buscar): el temporizador
  usa `ScannerDebounceMs` y solo actúa con coincidencia exacta de código/SKU.
- **Instancia única**: un solo proceso por carpeta de datos (`SingleInstance`). Un segundo
  intento muestra un aviso y se cierra; nunca abras dos cajas sobre la misma DB.
- **Accesibilidad**: F1–F8 navegan, F9 cobra, Ctrl+F enfoca la búsqueda, F12 abre los
  atajos, Ctrl+L cierra sesión y Ctrl+Shift+L bloquea. En el pago, F1–F5 eligen método y
  Ctrl+Enter agrega el pago. Los campos nuevos usan `Watermark`, nunca texto por defecto.
- **Bloqueo por inactividad**: `AutoLockMinutes` (por defecto 5; 0 desactiva) bloquea la
  caja y exige el PIN del usuario actual (`LockService` + `LockWindow`). No permitas cerrar
  la ventana de bloqueo sin desbloquear o cerrar sesión.
- **Vista previa**: `PreviewReceiptBeforePrint` muestra el comprobante antes de imprimir
  (`ReceiptPreviewWindow`). Se imprime automáticamente a los 3 segundos y se cierra,
  aunque no haya impresora; `Enter` imprime al instante y `Esc` cancela. El error se
  muestra en la ventana de venta completada. Cualquier cambio en el recibo debe verse
  también en `ReceiptFormatter.FormatPreview`.
- **Post-venta no bloqueante**: `SaleCompletedWindow` es una sola instancia no modal.
  Se cierra al escanear o agregar el siguiente producto, al navegar o con `Nueva venta`.
  No permitas dos vistas previas ni dos ventanas de venta completada a la vez.
- **SKU y códigos**: el SKU se genera con nombre + código de barras (`InventoryService.GenerateSku`).
  Al archivar un producto se liberan su SKU y sus códigos de barras.
- **Feedback**: sonidos de escaneo encontrado/nuevo/error, mensajes claros y estados
  vacíos útiles. Nunca mostrar excepciones crudas.
- **Colores/tema**: tokens en `App.axaml` y `Assets/Styles.axaml`; probar claro y oscuro.
- **Accesibilidad**: alto contraste, textos legibles, no depender solo del color.
- **Escalado**: `Ajustes → Apariencia → Escala de interfaz` modifica `AppFontSize`.

## 8. Pruebas obligatorias por tipo de cambio

| Cambio | Pruebas mínimas |
| --- | --- |
| Matemática/descuentos/impuestos | `RixelPOS.Domain.Tests` |
| Ventas, stock, crédito, caja | `RixelPOS.Infrastructure.Tests` |
| Hardware/ESC/POS | Test de formatter/transporte `file:` + prueba manual |
| Migración/consultas | Test de integración con SQLite temporal |
| UI | Compilar, abrir, redimensionar, probar DPI alto y ambos idiomas |
| Packaging | Ejecutar `build.sh` y verificar artefactos |

Ejecuta siempre:

```bash
dotnet build RixelPOS.sln -c Release
dotnet test RixelPOS.sln -c Release
```

## 9. Releases y versionado (automático)

- Version base en `Directory.Build.props` (`<Version>`).
- Workflow `.github/workflows/release.yml`:
  - Push a `main` → release **`latest`** con la versión base + número de ejecución.
  - Push de tag `v*` → release versionada e inmutable.
  - `workflow_dispatch` → versión manual.
- Artefactos: AppImage, `.deb`, tar.gz, `.pkg.tar.zst`, PKGBUILD, ZIP Windows e
  instalador Inno Setup, más `SHA256SUMS.txt`.
- El repositorio pacman se publica en la rama `gh-pages` en `/pacman/x86_64`:
  ```
  [rixelpos]
  SigLevel = Optional TrustAll
  Server = https://rixeldev.github.io/rixelpos/pacman/$arch
  ```
  `sudo pacman -Syu rixelpos`.
- `.NET SDK` es **opcional** (la app es self-contained). El instalador Windows ofrece
  una tarea opcional y el paquete Linux incluye `rixelpos-install-dotnet`.
- No subas releases a mano si el workflow puede hacerlo. No publiques desde un fork.
- Si cambias nombres de artefactos o el repo pacman, actualiza
  `docs/RELEASES.md`, este archivo y los scripts.
- **Flujo del agente al cerrar un lote de cambios importantes:**
  1. Subir `<Version>` en `Directory.Build.props` (parche para correcciones, minor para
     funciones o cambios de comportamiento).
  2. `dotnet build RixelPOS.sln -c Release` y `dotnet test RixelPOS.sln -c Release`.
  3. Actualizar `README.md`, `docs/` y este archivo si cambió algo visible.
  4. Commit descriptivo y `git push origin main` → publica la release `latest`.
  5. `git tag vX.Y.Z && git push origin vX.Y.Z` → publica la release versionada.
- El job `pacman-repo` está serializado (`concurrency: pacman-repo`) para que dos pushes
  seguidos no compitan por la rama `gh-pages`.
- Los paquetes Arch **no deben incluir un `/lib` de nivel superior** (choca con el paquete
  `filesystem`). Las reglas udev van en `/usr/lib/udev/rules.d`; lo mismo aplica al `.deb`
  en sistemas con `usrmerge`.

## 10. Seguridad y datos

- Contraseñas/PIN con bcrypt; nunca texto plano.
- Aprobación de encargado para anulación/descuentos altos según ajustes.
- SQLite WAL + transacciones; `VACUUM INTO` para backups.
- Restaurar valida `PRAGMA integrity_check` y las tablas esenciales antes de copiar; crea
  un backup de seguridad y exige reinicio. Un archivo inválido se rechaza.
- Revisa `SECURITY.md` antes de cambiar autenticación, backups o red.
- No añadas telemetría ni llamadas externas sin consentimiento y documentación.

## 11. Checklist antes de cerrar una tarea

- [ ] Compila en Release sin errores.
- [ ] Tests pasan.
- [ ] Textos nuevos en `es.json` y `en.json`.
- [ ] UI probada en claro/oscuro y con ventana redimensionada/DPI.
- [ ] Sin títulos/etiquetas duplicadas y con controles centrados.
- [ ] Migración aditiva si cambió la DB.
- [ ] README/docs/AGENTS actualizados si cambió arquitectura, comandos o releases.
- [ ] Sin secretos, DBs, logs ni artefactos `bin/obj/dist` en el commit.
- [ ] Commit claro; push a `main` y tag `vX.Y.Z` cuando el lote de cambios sea importante.

## 12. Limitaciones actuales (actualizar si se implementan)

- Firma/envío **e-CF DGII** no implementado; el módulo NCF genera comprobante y datos.
- Sincronización multterminal/nube diseñada pero no implementada.
- Reembolsos parciales no implementados; existe anulación completa con restitución de
  stock y crédito.
- Búsqueda online de productos no implementada (la recepción funciona offline).
- Impresión de etiquetas usa el ancho del recibo configurado; etiquetas dedicadas
  requieren ajustar el papel/impresora.
- Autoactualización silenciosa no implementada; el workflow publica releases y el
  usuario instala la nueva versión.
