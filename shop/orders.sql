CREATE TABLE IF NOT EXISTS DataProtectionKeys (
    Id int NOT NULL AUTO_INCREMENT PRIMARY KEY,
    FriendlyName longtext,
    Xml longtext
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS orders (
    Id char(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
    Owner varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    RequestHash char(64) NOT NULL,
    CreatedUtc datetime(6) NOT NULL,
    ExpiresUtc datetime(6) NOT NULL,
    Status varchar(16) NOT NULL,
    StripeSessionId varchar(200) UNIQUE,
    CheckoutUrl text,
    ReceiptUrl text,
    Email varchar(254) NOT NULL,
    DeliveryJson text NOT NULL,
    ShippingCents int NOT NULL,
    TotalCents int NOT NULL,
    PendingSlot tinyint GENERATED ALWAYS AS (CASE WHEN Status = 'pending' THEN 1 ELSE NULL END) STORED,
    UNIQUE (Owner, PendingSlot),
    INDEX (Owner, CreatedUtc),
    INDEX (Status, ExpiresUtc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS order_lines (
    OrderId char(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    ProductId varchar(64) NOT NULL,
    SizeCode varchar(12) NOT NULL,
    Name varchar(120) NOT NULL,
    PriceCents int NOT NULL,
    Quantity int NOT NULL CHECK (Quantity > 0),
    PRIMARY KEY (OrderId, ProductId, SizeCode),
    FOREIGN KEY (OrderId) REFERENCES orders(Id),
    FOREIGN KEY (ProductId, SizeCode) REFERENCES stock(ProductId, SizeCode)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS shipments (
    OrderId char(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
    Status varchar(16) NOT NULL,
    TrackingNumber varchar(32),
    LabelUrl text,
    FOREIGN KEY (OrderId) REFERENCES orders(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS customer_addresses (
    Owner varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin PRIMARY KEY,
    FirstName varchar(60) NOT NULL,
    LastName varchar(60) NOT NULL,
    Street varchar(40) NOT NULL,
    Complement varchar(30) NOT NULL,
    Postcode char(5) NOT NULL,
    City varchar(30) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS order_payments (
    OrderId char(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
    AmountPaid int NOT NULL,
    DiscountCents int NOT NULL,
    RefundedCents int NOT NULL DEFAULT 0,
    InvoiceUrl text,
    PaymentIntentId varchar(200) UNIQUE,
    FOREIGN KEY (OrderId) REFERENCES orders(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS return_requests (
    OrderId char(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
    Details varchar(1000) NOT NULL,
    Status varchar(24) NOT NULL DEFAULT 'requested',
    CreatedUtc datetime(6) NOT NULL,
    FOREIGN KEY (OrderId) REFERENCES orders(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
