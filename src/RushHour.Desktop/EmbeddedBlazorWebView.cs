using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace RushHour.Desktop;

// Assets live in the application assembly so the exe is portable even when
// launched from another working directory or extracted by the single-file host.
public sealed class EmbeddedBlazorWebView : BlazorWebView
{
    public override IFileProvider CreateFileProvider(string contentRootDir) => new Assets();

    private sealed class Assets : IFileProvider
    {
        private readonly Assembly assembly = typeof(EmbeddedBlazorWebView).Assembly;
        public IFileInfo GetFileInfo(string subpath)
        {
            string resource = "RushHour.Desktop.wwwroot." + subpath.TrimStart('/').Replace('/', '.');
            using var stream = assembly.GetManifestResourceStream(resource);
            return stream is null ? new NotFoundFileInfo(subpath) : new Asset(assembly, resource, Path.GetFileName(subpath), stream.Length);
        }
        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }
    private sealed class Asset(Assembly assembly, string resource, string name, long length) : IFileInfo
    {
        public bool Exists => true;
        public long Length => length;
        public string? PhysicalPath => null;
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => assembly.GetManifestResourceStream(resource)!;
    }
}
