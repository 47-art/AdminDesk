using System.Text.RegularExpressions;
using AdminDesk.Application.Documents;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Exceptions;

namespace AdminDesk.Infrastructure.Documents;

// Uploaded bytes on local disk, in one folder under the data directory. Only server-generated
// names are accepted, and every resolved path must stay inside the folder.
public sealed partial class DocumentFileStore : IDocumentFileStore
{
    private readonly string _root;

    public DocumentFileStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(string storedName, Stream content, CancellationToken ct)
    {
        var path = Resolve(storedName);
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(target, ct);
    }

    public Stream OpenRead(string storedName)
    {
        var path = Resolve(storedName);
        if (!File.Exists(path))
        {
            throw new NotFoundException("Document not found.", ErrorCodes.NOT_FOUND);
        }
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
    }

    public void DiscardUnsaved(string storedName)
    {
        try
        {
            var path = Resolve(storedName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Nothing else can be done for a file that cannot be removed; the metadata was never saved.
        }
    }

    private string Resolve(string storedName)
    {
        if (!StoredNamePattern().IsMatch(storedName))
        {
            throw new InvalidOperationException("Not a stored document name.");
        }
        var path = Path.GetFullPath(Path.Combine(_root, storedName));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The document path is outside the documents folder.");
        }
        return path;
    }

    [GeneratedRegex("^[0-9a-f]{32}\\.[a-z0-9]{2,5}$")]
    private static partial Regex StoredNamePattern();
}
