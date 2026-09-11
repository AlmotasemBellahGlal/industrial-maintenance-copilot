using Asset = IndustrialCopilot.Domain.Equipment.Equipment;

namespace IndustrialCopilot.Domain.Tests.Equipment;

public class EquipmentTests
{
    [Fact]
    public void PreservesAssetIdentityAndName()
    {
        var id = Guid.NewGuid();
        var equipment = new Asset(id, "Pump P-101");
        Assert.Equal(id, equipment.Id);
        Assert.Equal("Pump P-101", equipment.Name);
    }

    [Fact]
    public void RejectsEmptyIdentity() =>
        Assert.Throws<ArgumentException>(() => new Asset(Guid.Empty, "Pump P-101"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingName(string? name) =>
        Assert.ThrowsAny<ArgumentException>(() => new Asset(Guid.NewGuid(), name!));
}
