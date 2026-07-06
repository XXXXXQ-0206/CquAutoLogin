using System.Drawing;
using System.IO;
using DrawingSize = System.Drawing.Size;

namespace CquAutoLogin.Services;

public static class IconUtilities
{
    public static Icon LoadBaseIcon(string sourcePath)
    {
        if (Path.GetExtension(sourcePath).Equals(".ico", StringComparison.OrdinalIgnoreCase))
        {
            return new Icon(sourcePath, new DrawingSize(32, 32));
        }

        return Icon.ExtractAssociatedIcon(sourcePath) ?? (Icon)SystemIcons.Application.Clone();
    }
}
