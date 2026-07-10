namespace Bekci.Domain.Entities;

public sealed class Organization : Entity
{
    public string Name { get; private set; } = default!;

    private Organization() { }

    public static Organization Create(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name
    };
}
