-- Repairs existing local databases created before the Identity ownership migration.
-- Mirrors the Resources changes in 20261104083254_LinkResourceToIdentityUser.
-- Does not mark EF migrations as applied; migration history needs a separate baseline.

-- Keeps older resources valid until their owner can be verified.
ALTER TABLE `Resources`
    ADD COLUMN IF NOT EXISTS `OwnerUserId` varchar(255)
        CHARACTER SET utf8mb4 COLLATE utf8mb4_uca1400_ai_ci NULL;

-- Supports resource lookups by the permanent Identity user ID.
CREATE INDEX IF NOT EXISTS `IX_Resources_OwnerUserId`
    ON `Resources` (`OwnerUserId`);

-- Adds the migration's relationship once, without deleting users or resources.
SET @owner_fk_exists = (
    SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE()
      AND TABLE_NAME = 'Resources'
      AND CONSTRAINT_NAME = 'FK_Resources_AspNetUsers_OwnerUserId'
      AND CONSTRAINT_TYPE = 'FOREIGN KEY'
);
SET @owner_fk_sql = IF(@owner_fk_exists = 0,
    'ALTER TABLE `Resources` ADD CONSTRAINT `FK_Resources_AspNetUsers_OwnerUserId` FOREIGN KEY (`OwnerUserId`) REFERENCES `AspNetUsers` (`Id`)',
    'SELECT 1');
PREPARE owner_fk_statement FROM @owner_fk_sql;
EXECUTE owner_fk_statement;
DEALLOCATE PREPARE owner_fk_statement;

-- Checks the new field without exposing resource or account details.
SELECT COUNT(*) AS ResourceCount,
       COUNT(`OwnerUserId`) AS ResourcesWithIdentityOwner
FROM `Resources`;
