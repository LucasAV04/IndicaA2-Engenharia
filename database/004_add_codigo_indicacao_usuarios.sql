ALTER TABLE usuarios
    ADD COLUMN codigo_indicacao VARCHAR(8) NULL AFTER email,
    ADD CONSTRAINT uq_usuarios_codigo_indicacao UNIQUE (codigo_indicacao);
