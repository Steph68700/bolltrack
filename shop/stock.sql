CREATE TABLE IF NOT EXISTS shop_migrations (
    Version varchar(64) PRIMARY KEY
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS products (
    Id varchar(64) PRIMARY KEY,
    Name varchar(120) NOT NULL,
    PriceCents int NOT NULL CHECK (PriceCents >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS stock (
    ProductId varchar(64) NOT NULL,
    SizeCode varchar(12) NOT NULL,
    Available int NOT NULL CHECK (Available >= 0),
    PRIMARY KEY (ProductId, SizeCode),
    FOREIGN KEY (ProductId) REFERENCES products(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

START TRANSACTION;
INSERT IGNORE INTO shop_migrations VALUES ('inventaire-organisateur-2026-11-19');
SET @import_stock = ROW_COUNT();
INSERT INTO products (Id, Name, PriceCents)
SELECT * FROM (
    SELECT 'pull-2026-violet' id, 'Pull BollTrack 2026 Violet' nom, 6000 prix
    UNION ALL SELECT 'pull-2026-jaune', 'Pull BollTrack 2026 Jaune', 6000
    UNION ALL SELECT 'bolltrack-2026-violet', 'T-shirt BollTrack 2026 Violet', 2000
    UNION ALL SELECT 'bolltrack-2026-jaune', 'T-shirt BollTrack 2026 Jaune', 2000
    UNION ALL SELECT 'bolltrack-2025', 'T-shirt BollTrack 2025', 2000
    UNION ALL SELECT 'omen-haze-homme', 'T-shirt Omen Haze Homme', 2000
    UNION ALL SELECT 'omen-haze-femme', 'T-shirt Omen Haze Femme', 2000
    UNION ALL SELECT 'casquette-2025', 'Casquette BollTrack 2025', 1500
    UNION ALL SELECT 'bonnet-hivernal-2026', 'Bonnet BollTrack 2026', 2000
) AS catalogue WHERE @import_stock = 1;
-- Le T-shirt 2025 est épuisé. Aucune taille n'a été communiquée pour cette référence.
INSERT INTO stock (ProductId, SizeCode, Available)
SELECT * FROM (
    SELECT 'pull-2026-violet' produit, 'M' taille, 0 quantite
    UNION ALL SELECT 'pull-2026-violet', 'L', 3
    UNION ALL SELECT 'pull-2026-violet', 'XL', 4
    UNION ALL SELECT 'pull-2026-violet', '2XL', 5
    UNION ALL SELECT 'pull-2026-jaune', 'M', 2
    UNION ALL SELECT 'pull-2026-jaune', 'L', 11
    UNION ALL SELECT 'pull-2026-jaune', 'XL', 5
    UNION ALL SELECT 'pull-2026-jaune', '2XL', 4
    UNION ALL SELECT 'bolltrack-2026-violet', 'M', 6
    UNION ALL SELECT 'bolltrack-2026-violet', 'L', 10
    UNION ALL SELECT 'bolltrack-2026-violet', 'XL', 7
    UNION ALL SELECT 'bolltrack-2026-violet', '2XL', 11
    UNION ALL SELECT 'bolltrack-2026-jaune', 'M', 5
    UNION ALL SELECT 'bolltrack-2026-jaune', 'L', 12
    UNION ALL SELECT 'bolltrack-2026-jaune', 'XL', 10
    UNION ALL SELECT 'bolltrack-2026-jaune', '2XL', 5
    UNION ALL SELECT 'omen-haze-homme', 'S', 7
    UNION ALL SELECT 'omen-haze-homme', 'M', 20
    UNION ALL SELECT 'omen-haze-homme', 'L', 22
    UNION ALL SELECT 'omen-haze-homme', 'XL', 21
    UNION ALL SELECT 'omen-haze-homme', '2XL', 1
    UNION ALL SELECT 'omen-haze-homme', '3XL', 4
    UNION ALL SELECT 'omen-haze-femme', 'S', 6
    UNION ALL SELECT 'omen-haze-femme', 'M', 20
    UNION ALL SELECT 'omen-haze-femme', 'L', 17
    UNION ALL SELECT 'omen-haze-femme', 'XL', 5
    UNION ALL SELECT 'omen-haze-femme', '2XL', 0
    UNION ALL SELECT 'omen-haze-femme', '3XL', 0
    UNION ALL SELECT 'casquette-2025', 'TU', 5
    UNION ALL SELECT 'bonnet-hivernal-2026', 'TU', 16
) AS variantes WHERE @import_stock = 1;
COMMIT;
