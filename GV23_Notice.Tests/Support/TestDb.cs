using GV23_Notice.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GV23_Notice.Tests.Support
{
    /// <summary>
    /// In-memory AppDbContext for service and controller tests.
    ///
    /// Use one <see cref="Name"/> per test and call <see cref="NewContext"/>
    /// for each step (seed / act / assert) — like separate web requests —
    /// so EF change tracking from one step cannot hide a bug in the next.
    /// </summary>
    public sealed class TestDb
    {
        public string Name { get; } = $"enotice-tests-{Guid.NewGuid():N}";

        public AppDbContext NewContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Name)
                // NoticeSettingsService.CreateDraftAsync opens a transaction;
                // the in-memory provider has none, so ignore that warning.
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new AppDbContext(options);
        }

        /// <summary>Seed with a fresh context and save.</summary>
        public void Seed(Action<AppDbContext> seed)
        {
            using var db = NewContext();
            seed(db);
            db.SaveChanges();
        }
    }
}
