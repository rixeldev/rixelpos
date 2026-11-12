# Releases, instalación y actualización

## 1. Descargas automáticas

Cada push a `main` y cada tag `v*` ejecutan `.github/workflows/release.yml`:

| Evento | Resultado |
| --- | --- |
| push a `main` | actualiza la release **`latest`** con la última compilación |
| tag `v1.2.0` | crea la release versionada `v1.2.0` |
| `workflow_dispatch` | publica la versión indicada |

Artefactos publicados:

- `RixelPOS-<version>-x86_64.AppImage`
- `rixelpos_<version>_amd64.deb`
- `rixelpos-<version>-linux-x64.tar.gz` (incluye `install.sh`)
- `rixelpos-<version>-1-x86_64.pkg.tar.zst`
- `PKGBUILD`
- `RixelPOS-<version>-win-x64.zip`
- `RixelPOS-Setup-<version>.exe` (Inno Setup)
- `SHA256SUMS.txt`

Descarga: `https://github.com/rixeldev/rixelpos/releases/latest`.

El repositorio es privado; inicia sesión en GitHub para descargar.

## 2. CachyOS / Arch Linux

### Opción A — repositorio pacman (recomendado, `pacman -S`)

El workflow publica el repositorio en la rama `gh-pages`. En GitHub:
**Settings → Pages → Build and deployment → Deploy from a branch → `gh-pages` / root**.

Añade a `/etc/pacman.conf`:

```ini
[rixelpos]
SigLevel = Optional TrustAll
Server = https://rixeldev.github.io/rixelpos/pacman/$arch
```

Instala o actualiza:

```bash
sudo pacman -Syu
sudo pacman -S rixelpos
```

### Opción B — paquete directo

```bash
sudo pacman -U rixelpos-1.0.0-1-x86_64.pkg.tar.zst
```

### Opción C — AUR (tras publicar el PKGBUILD)

```bash
yay -S rixelpos
# o:
git clone https://aur.archlinux.org/rixelpos.git
cd rixelpos && makepkg -si
```

### Opción D — portable

```bash
tar -xzf rixelpos-<version>-linux-x64.tar.gz
cd rixelpos-<version>-linux-x64
./install.sh
# instalación del sistema:
sudo ./install.sh --system
```

### Opción E — AppImage / .deb

```bash
chmod +x RixelPOS-<version>-x86_64.AppImage
./RixelPOS-<version>-x86_64.AppImage

sudo apt install ./rixelpos_<version>_amd64.deb
```

El AppImage no necesita instalación; el `.deb` crea lanzador, icono y reglas udev.

## 3. Windows 10/11

1. Descarga `RixelPOS-Setup-<version>.exe`.
2. Ejecuta el instalador; crea accesos directos y aparece en el menú Inicio.
3. Alternativa portable: descomprime `RixelPOS-<version>-win-x64.zip` y ejecuta
   `RixelPOS.exe`.

Firma opcional del release (desde un entorno con el certificado, nunca en el repo):

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
  /f $env:CERT_PATH /p $env:CERT_PASSWORD dist\windows\RixelPOS-Setup-<version>.exe
```

## 4. .NET SDK automático / opcional

**RixelPOS es self-contained: no necesita .NET instalado para funcionar.** Esto es
deliberado para que la caja funcione offline desde el primer arranque.

Aun así, si quieres instalar el **SDK** para desarrollo, compilación o futuras
actualizaciones:

### Linux
```bash
rixelpos-install-dotnet            # incluido en el paquete
# o desde el repositorio:
./packaging/prerequisites/install-dotnet.sh 8.0
./packaging/prerequisites/install-dotnet.sh 10.0
```
Detecta `pacman`, `apt`, `dnf` o `zypper`; si no encuentra el paquete, usa
`dotnet-install.sh` oficial.

### Windows
El instalador marca por defecto la tarea **“Instalar .NET SDK 8 si falta”** (se puede desmarcar; requiere internet).
También puedes ejecutar:
```powershell
powershell -ExecutionPolicy Bypass -File packaging\prerequisites\install-dotnet.ps1 -Channel 8.0
```

El instalador nunca falla si el SDK no se puede descargar: la app self-contained ya está
instalada y funciona.

## 5. Verificar un release

```bash
sha256sum -c SHA256SUMS.txt
```

En Windows:

```powershell
Get-FileHash .\RixelPOS-Setup-*.exe -Algorithm SHA256
```

## 6. Actualizar

- **Linux pacman**: `sudo pacman -Syu`.
- **AppImage**: descarga la nueva versión y reemplaza el archivo.
- **.deb**: `sudo apt install ./rixelpos_<nueva>_amd64.deb`.
- **Windows**: ejecuta el nuevo instalador sobre la versión anterior.
- Los datos y ajustes se conservan; los respaldos automáticos siguen activos.

## 7. Versionado

- `Directory.Build.props` contiene la versión base.
- En `main` se publica `latest` con la base + número de ejecución de GitHub.
- Para una versión estable crea y sube un tag:
  ```bash
  git tag v1.2.0
  git push origin v1.2.0
  ```
- El workflow compila Linux y Windows, genera el repositorio pacman y publica los
  artefactos sin intervención manual.

## 8. Notas de seguridad

- No publiques releases desde un fork.
- No subas certificados de firma; usa secretos de GitHub (`CERT_PATH`, etc.) en el
  entorno de release.
- El repositorio pacman usa `SigLevel = Optional TrustAll`; si más adelante se firma el
  repositorio, cambia esa línea por la clave pública correspondiente.
- Los artefactos no incluyen bases de datos, logs ni datos de clientes.
