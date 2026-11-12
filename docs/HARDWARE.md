# Hardware compatible con RixelPOS

RixelPOS separa el hardware en interfaces (`Application/Printing/Contracts.cs`) y
adaptadores (`Infrastructure/Hardware`). Todo lo que no es ESC/POS estándar se puede
sustituir sin tocar la lógica de ventas.

## 1. Escáneres de código de barras

### Modo teclado (recomendado, USB/HID)
La mayoría de escáneres USB se comportan como un teclado. No hay que configurar nada:

1. Conecta el escáner.
2. En **Vender**, el cursor vive en el buscador.
3. Escanea. El código se escribe y `Enter` lo agrega.
4. En **Inventario → Recibir mercancía** y **Conteo físico**, el campo de escaneo
   recibe el foco al abrir la pestaña.

RixelPOS reproduce un sonido distinto para producto encontrado, producto nuevo y error,
según el archivo `Assets/Sounds/*.wav`. El antiduplicado usa `Ajustes → Recibos →
Anti-duplicado` (valor por defecto 350 ms).

### Modo serial
Configura `Ajustes → Recibos → Escáner` con:

```
serial:/dev/ttyUSB0@9600      # Linux
serial:COM3@9600              # Windows
```

El puerto se abre al iniciar sesión. Los códigos terminados en CR/LF se enrutan a la
pantalla activa (venta, recepción o conteo).

### Simbologías
El parseo de códigos es agnóstico: EAN-13, EAN-8, UPC-A, Code 128, Code 39 y QR se
tratan como texto. Para códigos internos se genera EAN-13 con prefijo `200` y dígito
verificador válido. Las etiquetas se imprimen en Code 128 o EAN-13 según el código.

## 2. Impresoras térmicas ESC/POS

Formatos de conexión aceptados (Ajustes → Recibos):

| Formato | Ejemplo | Notas |
| --- | --- | --- |
| TCP | `tcp://192.168.1.50:9100` | Impresora Ethernet/WiFi con puerto raw 9100 |
| USB Windows | `usb:EPSON TM-T20III` | Nombre exacto del spooler |
| CUPS Linux | `cups:EPSON_TM_T20III` | Se envía con `lp -o raw` |
| Serial | `serial:/dev/ttyUSB0@19200` | 9600/19200/38400/115200 |
| Dispositivo Linux | `usb:/dev/usb/lp0` | Escritura directa al dispositivo |
| Archivo | `file:/tmp/rixelpos.bin` | Pruebas sin hardware |

Anchos: 58 mm (32 columnas) y 80 mm (48 columnas). El recibo incluye logo opcional,
datos del negocio, líneas, impuestos, pagos, cambio, QR raster y código de barras de la
factura. El cajón se abre con `ESC p 0 25 250`.

### Linux / CachyOS
1. Instala CUPS y el driver/paquete de tu impresora:
   ```bash
   sudo pacman -S cups cups-filters
   sudo systemctl enable --now cups
   ```
2. Añade la impresora en `http://localhost:631` o con `lpadmin`.
3. Comprueba que responde:
   ```bash
   lpstat -p
   lp -d NOMBRE -o raw /dev/null
   ```
4. En RixelPOS usa `cups:NOMBRE` y pulsa **Probar impresora**.
5. Si usas USB directo:
   ```bash
   ls -l /dev/usb/lp*
   sudo usermod -aG lp $USER
   ```
   Cierra sesión y vuelve a entrar. El paquete incluye una regla udev en
   `packaging/linux/udev/99-rixelpos.rules`:
   ```
   SUBSYSTEM=="usb", ATTRS{idVendor}=="04b8", MODE="0660", GROUP="lp"
   ```
   Ajusta el VID/PID real de tu impresora.
6. CUPS raw evita que el driver rasterice el ticket. Si ves caracteres extraños,
   selecciona una cola raw (`lpadmin -p RixelRaw -E -v usb://... -m raw`).

### Windows 10/11
1. Instala el driver del fabricante (Epson, Xprinter, Bixolon, Star, etc.).
2. Comprueba el nombre exacto:
   ```powershell
   Get-Printer | Select-Object Name, DriverName, PortName
   ```
3. Usa `usb:NOMBRE EXACTO`. RixelPOS envía el trabajo como **RAW** al spooler usando
   `winspool.drv`; no pasa por el driver gráfico.
4. Si el puerto es `USB001` pero el nombre es distinto, usa el nombre, no el puerto.
5. Si el equipo no tiene driver, instala el genérico ESC/POS o usa una impresora de red.

### Pruebas
`Ajustes → Recibos → Imprimir prueba` envía un ticket corto. `Probar cajón` envía solo
el pulso. Los errores aparecen como mensajes humanos; el detalle técnico queda en
`logs/`.

## 3. Cajón de dinero

Conecta el cajón al puerto RJ-11/RJ-12 de la impresora. RixelPOS envía el pulso ESC/POS.
Opciones:
- **Abrir cajón al cobrar en efectivo** (por defecto activo).
- **Cajón de dinero** activa/desactiva el envío.

## 4. Etiquetas de producto

`Inventario → Etiqueta` imprime nombre, precio, SKU y código de barras. Usa la misma
impresora térmica y el ancho configurado. Para etiquetas dedicadas (p. ej. 40×30 mm)
ajusta el tamaño de papel en la impresora; RixelPOS usa el ancho de recibo configurado.

## 5. Pantalla de cliente (opcional)

`Ajustes → Recibos → Pantalla de cliente`, conexión serial `serial:/dev/ttyUSB1@9600`.
Se envía un texto de 20 columnas con `\f` para limpiar. Muchas pantallas VFD aceptan
ESC/POS; si la tuya usa otro protocolo, implementa `ICustomerDisplay`.

## 6. Solución de problemas

| Síntoma | Causa probable | Solución |
| --- | --- | --- |
| “No se pudo conectar” | IP/puerto incorrectos o impresora apagada | Verifica IP, puerto 9100 y ping |
| “Permiso denegado” en Linux | Usuario fuera del grupo `lp` | `sudo usermod -aG lp $USER`, cierra sesión |
| “Windows no encontró el controlador” | Nombre del spooler incorrecto | `Get-Printer` y usa el nombre exacto |
| Imprime basura | Driver gráfico/CUPS rasterizando | Usa cola raw / `-o raw` |
| No corta | Impresora sin cutter | El ticket igualmente termina; configura corte en la impresora |
| QR no sale | Firmware sin raster | Actualiza firmware o usa impresora compatible ESC/POS |
| Escáner no agrega | Foco fuera del buscador | Haz clic en el buscador o vuelve a la pestaña |
| Escáner serial no responde | Baudios o puerto incorrectos | Prueba 9600/19200 y revisa permisos |
| Cajón no abre | Conector o pulso deshabilitado | Prueba “Probar cajón”; revisa cable RJ-11 |

## 7. Modo de prueba sin hardware

Usa `file:/tmp/rixelpos.bin` como impresora. Cada recibo se escribe en ese archivo, lo que
permite validar el flujo completo sin dispositivos. Para el escáner, usa el buscador y
`Enter` como si fuera un teclado.
