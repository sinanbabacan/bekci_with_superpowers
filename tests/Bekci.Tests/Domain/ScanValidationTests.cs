using Bekci.Domain;
using FluentAssertions;
using Xunit;

namespace Bekci.Tests.Domain;

public class ScanValidationTests
{
    [Fact]
    public void Within_radius_is_valid()
    {
        // ~11 m north of the checkpoint, radius 25 m
        var ok = ScanValidation.IsWithinGeofence(40.0000, 29.0000, 25, 40.0001, 29.0000);
        ok.Should().BeTrue();
    }

    [Fact]
    public void Outside_radius_is_invalid()
    {
        // ~111 m north, radius 25 m
        var ok = ScanValidation.IsWithinGeofence(40.0000, 29.0000, 25, 40.0010, 29.0000);
        ok.Should().BeFalse();
    }

    [Fact]
    public void Missing_location_is_invalid()
    {
        ScanValidation.IsWithinGeofence(40.0, 29.0, 25, null, null).Should().BeFalse();
    }
}
