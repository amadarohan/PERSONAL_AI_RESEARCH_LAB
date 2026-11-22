namespace PersonalResearchLab.Application.Abstractions;

/// <summary>
/// Stores provider credentials outside the database so a database backup never contains keys.
/// The Application layer only needs presence checks and writes; the encryption strategy is an
/// infrastructure concern.
/// </summary>
public interface ISecretStore
{
    string? Get(string name);

    void Set(string name, string value);

    void Remove(string name);

    bool Contains(string name);
}
