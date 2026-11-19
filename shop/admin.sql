CREATE TABLE IF NOT EXISTS product_listings (
    ProductId varchar(64) PRIMARY KEY,
    Category varchar(20) NOT NULL,
    CollectionName varchar(70) NOT NULL DEFAULT '',
    Color varchar(40) NOT NULL DEFAULT '',
    Fit varchar(32) NOT NULL DEFAULT '',
    StaticImage varchar(160),
    PhotoVersion char(32),
    Published boolean NOT NULL DEFAULT true,
    Position int NOT NULL DEFAULT 100,
    Revision int NOT NULL DEFAULT 1,
    FOREIGN KEY (ProductId) REFERENCES products(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS product_photos (
    ProductId varchar(64) PRIMARY KEY,
    ContentType varchar(30) NOT NULL,
    Bytes mediumblob NOT NULL,
    FOREIGN KEY (ProductId) REFERENCES products(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS stock_movements (
    Seq bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
    OperationId char(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL UNIQUE,
    ProductId varchar(64) NOT NULL,
    SizeCode varchar(12) NOT NULL,
    Delta int NOT NULL,
    BalanceAfter int NOT NULL,
    Reason varchar(120) NOT NULL,
    Actor varchar(200) NOT NULL,
    CreatedUtc datetime(6) NOT NULL,
    INDEX (ProductId, Seq),
    FOREIGN KEY (ProductId, SizeCode) REFERENCES stock(ProductId, SizeCode)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

START TRANSACTION;
INSERT IGNORE INTO shop_migrations VALUES ('fiches-boutique-2026-11-19');
SET @seed_listings = ROW_COUNT();
INSERT INTO product_listings (ProductId,Category,CollectionName,Color,Fit,StaticImage,Position)
SELECT p.Id, s.category, s.collection_name, s.color, s.fit, s.image_path, s.sort_position
FROM (
    SELECT 'bolltrack-2026-violet' id,'tshirts' category,'BollTrack · 2026' collection_name,'Violet' color,'' fit,'tshirt-bolltrack-2.jpg' image_path,10 sort_position
    UNION ALL SELECT 'bolltrack-2026-jaune','tshirts','BollTrack · 2026','Jaune','','tshirt-bolltrack-1.jpg',20
    UNION ALL SELECT 'pull-2026-violet','pulls','BollTrack · 2026','Violet','','sweat-bolltrack-2.jpg',30
    UNION ALL SELECT 'pull-2026-jaune','pulls','BollTrack · 2026','Jaune','','sweat-bolltrack-1.jpg',40
    UNION ALL SELECT 'bonnet-hivernal-2026','accessoires','BollTrack · 2026','Noir','','bonnet-bolltrack-hivernal.jpg',50
    UNION ALL SELECT 'bolltrack-2025','tshirts','BollTrack · 2025','','','archive/06_09_2025/tshirt.jpg',60
    UNION ALL SELECT 'casquette-2025','accessoires','BollTrack · 2025','','',NULL,70
    UNION ALL SELECT 'omen-haze-homme','tshirts','Omen Haze','','Homme',NULL,80
    UNION ALL SELECT 'omen-haze-femme','tshirts','Omen Haze','','Femme',NULL,90
) AS s JOIN products AS p ON p.Id=s.id WHERE @seed_listings=1;
INSERT INTO product_listings (ProductId,Category)
SELECT p.Id,'accessoires' FROM products p LEFT JOIN product_listings l ON p.Id=l.ProductId
WHERE l.ProductId IS NULL AND @seed_listings=1;
COMMIT;
