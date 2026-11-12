-- At most one row: the fingerprint of the salt every person hash in the raw tables was made with
-- (IdentityHash.SaltFingerprint; it does not give the salt away). It is written in the same transaction
-- as the batch whose hashes it describes. No row while the store holds nobody.
CREATE TABLE person_hashing (
    salt_fingerprint TEXT NOT NULL
) STRICT;
