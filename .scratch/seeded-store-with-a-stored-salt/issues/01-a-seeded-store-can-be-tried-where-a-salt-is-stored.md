# 01: A seeded store can be tried on a laptop that has a hashing salt stored

**What to build:** Someone who wants to try a published `telltale` on synthetic data can do so on the laptop that also runs against real data. Today they cannot: `eng/SeedStore` hashes its fake people with the tests' salt, the published executable on Windows reads the hashing salt from Windows Credential Manager and from nowhere else, and where a salt is stored there `derive`, `report` and `query` stop because the two salts differ. The message they give advises `backfill`, which calls Jira and does not fit a seeded store. The stored salt must stay as it is, because the real store's people are hashed with it.

The README says so since the brief visit treatment run (its section on the seeded store). This ticket removes the limit, or settles that it stays.

Background: the final review of the brief visit treatment run, finding C1 (2026-11-17). It is older than that feature. The reviewer's end-to-end run had to call the published assemblies through a small host with an empty secret store.

**Blocked by:** None (can start immediately).

**Status:** needs-info

Waiting on the owner to choose between the ways below. Each of the last two changes how secrets are chosen on Windows, which the project constraints fix: secrets live in Windows Credential Manager or DPAPI-protected storage, and on Windows no environment variable is read for a secret.

## Ways to do it, not yet chosen

1. **Leave the limit and say so where the user meets it.** `SeedStore`'s closing text says a seeded store can be tried only where no hashing salt is stored, as the README does, and the refusal's advice no longer names `backfill` when the store is a seeded one. Nothing about secrets changes.
2. **A developer-only way to run with no secret store.** The executable gains a switch, or `SeedStore` writes a marker into the store, under which `derive`, `report` and `query` skip the salt check for a store that holds only synthetic people. The check must be impossible to skip for a real store.
3. **`SeedStore` publishes its own host.** A second small executable, built by `SeedStore`, calls the same assemblies with an empty secret store. The published `telltale` is unchanged.

## Acceptance criteria

To be written when a way is chosen. Whichever it is:

- [ ] On a Windows account that has a hashing salt stored, a freshly seeded folder can be derived, reported on and queried, or the tool and `SeedStore` both say plainly why not and what to do
- [ ] The stored salt is never changed, replaced or printed
- [ ] A real store's salt check cannot be skipped by the same route
- [ ] No message advises `backfill` for a seeded store
- [ ] The README's section on the seeded store agrees with what was built
- [ ] Tests use TUnit; all fixtures are synthetic
