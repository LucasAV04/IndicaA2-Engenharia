CREATE TABLE IF NOT EXISTS vistorias (
    id CHAR(36) NOT NULL,
    usuario_id CHAR(36) NOT NULL,
    tipo_planta VARCHAR(150) NOT NULL,
    area_m2 DECIMAL(10,2) NOT NULL,
    pacote INT NOT NULL,
    data_agendada DATETIME(6) NOT NULL,
    status INT NOT NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    CONSTRAINT pk_vistorias PRIMARY KEY (id),
    CONSTRAINT fk_vistorias_usuarios FOREIGN KEY (usuario_id) REFERENCES usuarios (id),
    INDEX ix_vistorias_usuario_id (usuario_id)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci;
