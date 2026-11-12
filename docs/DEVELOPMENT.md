# Desarrollo y mantenimiento

## Requisitos

- .NET SDK 8.0 (el `global.json` permite SDK posteriores con `rollForward`).
- Linux: X11/Wayland, ICU, fuentes, dependencias de Avalonia.
- Windows: Windows 10/11.

## Comandos

```bash
dotnet restore RixelPOS.sln
dotnet build RixelPOS.sln -c Release
dotnet test RixelPOS.sln -c Release
dotnet run --project src/RixelPOS.Desktop
```

Publicación local:

```bash
# Linux x64 self-contained
dotnet publish src/RixelPOS.Desktop/RixelPOS.Desktop.csproj -c Release \
  -r linux-x64 --self-contained true -o dist/linux/RixelPOS

# Windows x64 self-contained
dotnet publish src/RixelPOS.Desktop/RixelPOS.Desktop.csproj -c Release \
  -r win-x64 --self-contained true -o dist/windows/RixelPOS
```

## Dónde están los datos

Durante el desarrollo, usa una carpeta aislada para no tocar datos reales:

```bash
RIXELPOS_DATA_DIR=/tmp/rixelpos-dev dotnet run --project src/RixelPOS.Desktop
```

Rutas por defecto:

- Linux: `~/.local/share/rixelpos/`
- Windows: `%APPDATA%\rixelpos\`

Contenido: `rixelpos.db`, `backups/`, `logs/`, `documents/`.

## Base de datos y migraciones

- Motor: SQLite (Microsoft.Data.Sqlite) + Dapper.
- Migraciones: `src/RixelPOS.Infrastructure/Data/Migrations/*.sql`, embebidas y aplicadas
  por versión numérica al iniciar.
- Para añadir una migración crea `002_mi_cambio.sql`; no edites una migración ya aplicada
  en producción.
- La app activa WAL y foreign keys. Las operaciones compuestas usan
  `IPosStore.ExecuteInTransaction`.
- `SqliteExtensions` normaliza el casing de los parámetros porque Microsoft.Data.Sqlite es
  sensible a mayúsculas.

## Reset de una base de desarrollo

Cierra la app y elimina la carpeta de datos aislada (`RIXELPOS_DATA_DIR`) o solo
`rixelpos.db*`. La app volverá al asistente de configuración.

## Tests

```bash
dotnet test tests/RixelPOS.Domain.Tests
dotnet test tests/RixelPOS.Infrastructure.Tests
```

- Los tests de dominio no tocan disco ni UI.
- Los de infraestructura usan una carpeta temporal y una base SQLite nueva por test.
- `PdfService` usa SkiaSharp; el paquete `SkiaSharp.NativeAssets.*` correspondiente se
  incluye por sistema operativo en el csproj.

## Convenciones

- Dinero siempre en centavos (`long`); cantidades en milésimas (`stock_milli`).
- Errores de negocio con `BusinessRuleException` y mensajes humanos.
- Inyección de dependencias en `AppServices.cs`.
- Commands MVVM con CommunityToolkit; código de UI específico de ventanas en code-behind.
- Textos visibles con `{loc:Tr clave}` / `LocalizationService`; añade la clave a
  `Localization/es.json` y `en.json`.
- No introduzcas secretos. Usa variables de entorno o el gestor de secretos de la CI.

## Prerrequisitos automáticos

La app es self-contained; no necesita .NET para ejecutarse. Si necesitas el SDK:

```bash
./packaging/prerequisites/install-dotnet.sh 8.0     # o 10.0
powershell -ExecutionPolicy Bypass -File packaging/prerequisites/install-dotnet.ps1 -Channel 8.0
```

El script de Linux detecta `pacman`, `apt`, `dnf` o `zypper`; si no encuentra el paquete
usa `dotnet-install.sh`. El instalador de Windows marca la tarea por defecto y el paquete
Linux incluye `rixelpos-install-dotnet`.

## Empaquetado

```bash
./packaging/linux/build.sh 1.0.0
powershell -ExecutionPolicy Bypass -File packaging/windows/build.ps1 -Version 1.0.0
```

Los scripts no firman binarios. La firma se realiza con el certificado del cliente en el
entorno de release. No subas certificados ni contraseñas al repositorio.

El build de Linux genera además el paquete `.pkg.tar.zst`, el repositorio pacman en
`dist/linux/pacman/x86_64` y el instalador portable `install.sh`. Consulta
[RELEASES.md](RELEASES.md) y [AGENTS.md](../AGENTS.md) antes de tocar el workflow.

## Estilo de UI

- Tokens de tema en `App.axaml` y `Assets/Styles.axaml`.
- Ventana máxima útil = área de trabajo del monitor / escala DPI, ajustada por
  `WindowSizing`.
- Los formularios largos usan `ScrollViewer` y tamaños mínimos para que el contenido no se
  corte al redimensionar.
- Botones táctiles ≥ 40 px; acciones principales ≥ 54 px.
- Evita texto duplicado: la cabecera de la ventana ya muestra el nombre de la sección.
