using System.Globalization;
using System.Text;
using QRCoder;
using SkiaSharp;

namespace RixelPOS.Infrastructure.Hardware;

/// <summary>Builds ESC/POS command streams without any platform dependency.</summary>
public sealed class EscPosBuilder
{
    private readonly List<byte> _bytes = new();

    public EscPosBuilder Raw(params byte[] values)
    {
        _bytes.AddRange(values);
        return this;
    }

    public EscPosBuilder Initialize() => Raw(0x1B, 0x40);

    public EscPosBuilder Align(TextAlign align) => Raw(0x1B, 0x61, (byte)align);

    public EscPosBuilder Bold(bool on = true) => Raw(0x1B, 0x45, on ? (byte)1 : (byte)0);

    public EscPosBuilder Underline(bool on = true) => Raw(0x1B, 0x2D, on ? (byte)1 : (byte)0);

    public EscPosBuilder DoubleSize(bool on = true) => Raw(0x1D, 0x21, on ? (byte)0x11 : (byte)0x00);

    public EscPosBuilder Text(string text)
    {
        _bytes.AddRange(Encoding.ASCII.GetBytes(AsciiText(text)));
        return this;
    }

    public EscPosBuilder Line(string text = "")
    {
        Text(text);
        return Raw(0x0A);
    }

    public EscPosBuilder Feed(int lines = 1)
    {
        for (var i = 0; i < Math.Clamp(lines, 0, 20); i++)
        {
            Raw(0x0A);
        }

        return this;
    }

    public EscPosBuilder Separator(char character = '-', int width = 32) => Line(new string(character, width));

    public EscPosBuilder Cut(bool partial = true)
    {
        Raw(0x0A, 0x0A);
        return partial ? Raw(0x1D, 0x56, 0x42, 0x00) : Raw(0x1D, 0x56, 0x00);
    }

    public EscPosBuilder KickDrawer() => Raw(0x1B, 0x70, 0x00, 0x19, 0xFA);

    /// <summary>Prints a QR as a monochrome raster for maximum printer compatibility.</summary>
    public EscPosBuilder QrRaster(string payload, int moduleSize = 5, int quietZone = 4)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M, forceUtf8: true);
            var matrix = data.ModuleMatrix;
            var size = matrix.Count + quietZone * 2;
            var dotsPerModule = Math.Clamp(moduleSize, 2, 10);

            var widthDots = (size * dotsPerModule + 7) / 8 * 8;
            var heightDots = size * dotsPerModule;
            var bytesPerRow = widthDots / 8;
            var raster = new byte[bytesPerRow * heightDots];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var qx = x - quietZone;
                    var qy = y - quietZone;
                    var dark = qx >= 0 && qy >= 0 && qx < matrix.Count && qy < matrix.Count && matrix[qy][qx];
                    if (!dark)
                    {
                        continue;
                    }

                    for (var dy = 0; dy < dotsPerModule; dy++)
                    {
                        var py = y * dotsPerModule + dy;
                        for (var dx = 0; dx < dotsPerModule; dx++)
                        {
                            var px = x * dotsPerModule + dx;
                            raster[py * bytesPerRow + px / 8] |= (byte)(0x80 >> (px % 8));
                        }
                    }
                }
            }

            Raw(0x1D, 0x76, 0x30, 0x00,
                (byte)(bytesPerRow & 0xFF), (byte)(bytesPerRow >> 8),
                (byte)(heightDots & 0xFF), (byte)(heightDots >> 8));
            _bytes.AddRange(raster);
            return Line();
        }
        catch
        {
            return Line("[QR]");
        }
    }

    /// <summary>Renders a PNG/JPG logo as a monochrome raster.</summary>
    public EscPosBuilder LogoRaster(string path, int maxWidthDots)
    {
        try
        {
            if (!File.Exists(path))
            {
                return this;
            }

            using var bitmap = SKBitmap.Decode(path);
            if (bitmap is null)
            {
                return this;
            }

            var scale = Math.Min(1.0, (double)maxWidthDots / bitmap.Width);
            var width = Math.Max(1, (int)(bitmap.Width * scale));
            var height = Math.Max(1, (int)(bitmap.Height * scale));
            using var resized = bitmap.Resize(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), SKFilterQuality.High);
            if (resized is null)
            {
                return this;
            }

            var widthDots = (width + 7) / 8 * 8;
            var bytesPerRow = widthDots / 8;
            var raster = new byte[bytesPerRow * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var c = resized.GetPixel(x, y);
                    var alpha = c.Alpha / 255.0;
                    var luminance = (0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue) * alpha + 255 * (1 - alpha);
                    if (luminance < 160)
                    {
                        raster[y * bytesPerRow + x / 8] |= (byte)(0x80 >> (x % 8));
                    }
                }
            }

            Align(TextAlign.Center);
            Raw(0x1D, 0x76, 0x30, 0x00,
                (byte)(bytesPerRow & 0xFF), (byte)(bytesPerRow >> 8),
                (byte)(height & 0xFF), (byte)(height >> 8));
            _bytes.AddRange(raster);
            return Line();
        }
        catch
        {
            return this;
        }
    }

    public EscPosBuilder BarcodeCode128(string code, int height = 70)
    {
        var payload = "{B" + AsciiText(code);
        var data = Encoding.ASCII.GetBytes(payload);
        if (data.Length > 255)
        {
            return this;
        }

        Raw(0x1B, 0x61, 0x01, 0x1D, 0x48, 0x02, 0x1D, 0x68, (byte)Math.Clamp(height, 24, 255), 0x1D, 0x77, 0x02, 0x1D, 0x6B, 0x49, (byte)data.Length);
        _bytes.AddRange(data);
        return Line();
    }

    public EscPosBuilder BarcodeEan13(string code)
    {
        code = new string(code.Where(char.IsDigit).ToArray());
        if (code.Length == 13)
        {
            code = code[..12];
        }

        if (code.Length != 12)
        {
            return this;
        }

        Raw(0x1B, 0x61, 0x01, 0x1D, 0x48, 0x02, 0x1D, 0x68, 0x50, 0x1D, 0x77, 0x02, 0x1D, 0x6B, 0x43, 0x0C);
        _bytes.AddRange(Encoding.ASCII.GetBytes(code));
        return Line();
    }

    public byte[] Build() => _bytes.ToArray();

    public static string AsciiText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(ch switch
            {
                '“' or '”' or '«' or '»' => '"',
                '‘' or '’' => '\'',
                '–' or '—' => '-',
                '…' => '.',
                '€' => 'E',
                '₿' => 'B',
                '×' => 'x',
                '¿' => '?',
                '¡' => '!',
                _ => ch
            });
        }

        var result = sb.ToString();
        var ascii = new StringBuilder(result.Length);
        foreach (var ch in result)
        {
            ascii.Append(ch <= 126 ? ch : '?');
        }

        return ascii.ToString();
    }

    public enum TextAlign
    {
        Left = 0,
        Center = 1,
        Right = 2
    }
}
