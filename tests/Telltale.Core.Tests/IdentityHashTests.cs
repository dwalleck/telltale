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

    // Expected values computed with openssl, not with the code under test:
    // printf '<account>' | openssl dgst -sha256 -hmac '<salt>'
    [Test]
    [Arguments("fake-account-0001", "synthetic-salt-one", "f9fd2e04c99208cd4033758d1e35d2b3a37fc967d7e0ee2a4c500d2a8142941e")]
    [Arguments("fake-account-0001", "synthetic-salt-two", "5dd24bc546424219457808eae071bbf7d8896e0108fe561f0a8f4e224c4e6850")]
    [Arguments("fake-account-0002", "synthetic-salt-one", "6718063a6f22307ddc5e634af96bbf55fa967e99c80c9bf389c6d1883e599bed")]
    public async Task Each_account_and_salt_has_its_own_hash(string accountId, string salt, string expected)
    {
        await Assert.That(IdentityHash.Of(accountId, salt)).IsEqualTo(expected);
    }

    // printf 'telltale salt fingerprint' | openssl dgst -sha256 -hmac '<salt>'
    [Test]
    [Arguments("synthetic-salt-one", "ddd9d5eb5e8d1f52028ee5b9972e6e2a70fa201ab9e66a84198397e93f39bba5")]
    [Arguments("synthetic-salt-two", "d49d1f8d271fe6c8f08fc49fb5e76dba3aa7a82f2c6e5e42b678e57321b11344")]
    public async Task A_salts_fingerprint_is_a_digest_keyed_by_the_salt_so_it_tells_salts_apart_without_holding_one(string salt, string expected)
    {
        await Assert.That(IdentityHash.SaltFingerprint(salt)).IsEqualTo(expected);
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
