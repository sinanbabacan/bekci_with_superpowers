using Bekci.Domain;
using Bekci.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Bekci.Tests.Domain;

public class PatrolTests
{
    [Fact]
    public void Start_uses_client_supplied_id_and_is_in_progress()
    {
        var id = Guid.NewGuid();
        var startedAt = new DateTime(2026, 7, 10, 22, 0, 0, DateTimeKind.Utc);

        var patrol = Patrol.Start(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), startedAt);

        patrol.Id.Should().Be(id);
        patrol.Status.Should().Be(PatrolStatus.InProgress);
        patrol.StartedAt.Should().Be(startedAt);
        patrol.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Complete_sets_status_and_timestamp()
    {
        var patrol = Patrol.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 7, 10, 22, 0, 0, DateTimeKind.Utc));
        var completedAt = new DateTime(2026, 7, 10, 22, 45, 0, DateTimeKind.Utc);

        patrol.Complete(completedAt);

        patrol.Status.Should().Be(PatrolStatus.Completed);
        patrol.CompletedAt.Should().Be(completedAt);
    }
}
