using Telltale.Core.Identity;

namespace Telltale.Core.Tests;

public class IdentityHashTests
{
    [Test]
    public async Task The_hash_is_hmac_sha256_keyed_by_the_salt()
    {
        // RFC 4231, test case 2.
        var hash = IdentityHash.Of("what do ya want for nothing?", "Jefe");

        await Assert.That(hash).IsEqualTo("5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843");
    }

    [Test]
    public async Task The_same_account_and_salt_give_the_same_hash()
    {
        await Assert.That(IdentityHash.Of("fake-account-0001", "synthetic-salt-one"))
            .IsEqualTo(IdentityHash.Of("fake-account-0001", "synthetic-salt-one"));
    }

    [Test]
    public async Task A_different_salt_gives_a_different_hash()
    {
        await Assert.That(IdentityHash.Of("fake-account-0001", "synthetic-salt-one"))
            .IsNotEqualTo(IdentityHash.Of("fake-account-0001", "synthetic-salt-two"));
    }

    [Test]
    public async Task A_different_account_gives_a_different_hash()
    {
        await Assert.That(IdentityHash.Of("fake-account-0001", "synthetic-salt-one"))
            .IsNotEqualTo(IdentityHash.Of("fake-account-0002", "synthetic-salt-one"));
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task An_empty_salt_is_refused(string salt)
    {
        // Hashing with a known key would make every stored hash reversible by lookup.
        await Assert.That(() => IdentityHash.Of("fake-account-0001", salt)).Throws<ArgumentException>();
    }
}
