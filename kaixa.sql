-- Base de dades que simula entitat bancària

DROP DATABASE IF EXISTS kaixa;
CREATE DATABASE IF NOT EXISTS kaixa;
USE kaixa;

CREATE TABLE Usuaris (
    id INT AUTO_INCREMENT PRIMARY KEY,
    dni CHAR(9) UNIQUE NOT NULL,
    nom VARCHAR(30) NOT NULL,
    cognom VARCHAR(60) NOT NULL,
    adreca VARCHAR(120),
    telefon VARCHAR(12),
    username VARCHAR(30) UNIQUE NOT NULL,
    password VARCHAR(64) NOT NULL
);

CREATE TABLE Comptes (
    id INT AUTO_INCREMENT PRIMARY KEY,
    numero_compte VARCHAR(24) UNIQUE NOT NULL,
    data_creacio TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    -- titular VARCHAR(60),
    estat ENUM('ACTIU','BLOQUEJAT') DEFAULT 'ACTIU'
);

CREATE TABLE UsuarisComptes (
    usuari_id INT,
    compte_id INT,
    rol ENUM('TITULAR','AUTORITZAT') DEFAULT 'TITULAR',
    PRIMARY KEY (usuari_id, compte_id),
    FOREIGN KEY (usuari_id) REFERENCES Usuaris(id),
    FOREIGN KEY (compte_id) REFERENCES Comptes(id)
);

CREATE TABLE Moviments (
    id INT AUTO_INCREMENT PRIMARY KEY,
    compte_id INT,
    import DECIMAL(10,2),
    concepte VARCHAR(120),
    saldo DECIMAL(10,2),
    data TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (compte_id) REFERENCES Comptes(id)
);

CREATE TABLE Alertes (
    id INT AUTO_INCREMENT PRIMARY KEY,
    compte_id INT,
    missatge VARCHAR(100),
    data TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (compte_id) REFERENCES Comptes(id)
);

CREATE VIEW VistaSaldos AS
 SELECT m.compte_id, m.saldo
  FROM Moviments m WHERE m.id = 
   (SELECT MAX(id) FROM Moviments WHERE compte_id = m.compte_id);


-- Triggers *******************************************

DROP TRIGGER IF EXISTS before_insert_moviments;
DROP TRIGGER IF EXISTS after_insert_moviments;

-- Trigger per a bloquejar dades invàlides

DELIMITER //

CREATE TRIGGER before_insert_moviments
BEFORE INSERT ON Moviments
FOR EACH ROW
BEGIN
    DECLARE saldo_previ DECIMAL(10,2) DEFAULT 0;

    IF NEW.import = 0 THEN
        SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'Moviment zero no permès';
    END IF;
    
    IF NEW.compte_id IS NULL THEN
        SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'Compte obligatori';
    END IF; 

-- calcular darrer saldo
SELECT saldo INTO saldo_previ
    FROM Moviments WHERE (compte_id = NEW.compte_id)
    ORDER BY id DESC LIMIT 1;

SET saldo_previ = IFNULL(saldo_previ, 0); -- Sinó hi han moviments previs el saldo serà 0

SET NEW.saldo = saldo_previ + NEW.import; -- Nou saldo acumulat

END//



-- Trigger de control de moviments

CREATE TRIGGER after_insert_moviments
AFTER INSERT ON Moviments
FOR EACH ROW
BEGIN
    -- DECLARE saldo_actual DECIMAL(10,2);
    DECLARE saldo_previ DECIMAL(10,2);

    SET saldo_previ = NEW.saldo - NEW.import;

    IF saldo_previ >= 0 AND NEW.saldo < 0 THEN
        INSERT INTO Alertes(compte_id, missatge)
        VALUES (NEW.compte_id, 'Entrada en descobert');

      ELSEIF saldo_previ <= 10000 AND NEW.saldo > 10000 THEN
        INSERT INTO Alertes(compte_id, missatge)
        VALUES (NEW.compte_id, 'Supera llindar 10000');
    END IF;
END//
DELIMITER ;


/*
-- USUARIS operatius

Usuari tècnic que es valida a la BBDD i utilitza el rol d'un usuari client: cashbox_app
Pot crear usuaris, comptes i vincular-los

CREATE USER 'cashbox_app'@'%' IDENTIFIED BY 'app123';
GRANT SELECT, INSERT, UPDATE ON kaixa.* TO 'cashbox_app'@'%';

Operació d'un client:

SELECT COUNT(*) FROM UsuarisComptes WHERE usuari_id = ? AND compte_id = ?;

Si no és titular: Accés denegat

Els clients es guardaran a la taula Clients i el seu password es codificarà amb sha2 de longitud 256 bits

*/

