using OpensourceLab.FileStorage.ServedServices;

public sealed class VirtualPathAccessScope
{
    private readonly PathWildcardScope _scope;

    private VirtualPathAccessScope(IEnumerable<string> wildcards)
    {
        _scope = new PathWildcardScope(wildcards);
    }

    public static VirtualPathAccessScope PublicOnly()
    {
        return new(new[] { "/public/*" });
    }

    public static VirtualPathAccessScope FromWildcards(IEnumerable<string> wildcards)
    {
        return new(wildcards);
    }

    public bool IsAllowed(string path)
    {
        return _scope.IsAllowed(path);
    }
}