-- Extensão uuid-ossp disponível caso necessário
-- O EF Core usa gen_random_uuid() (pgcrypto/built-in) — este script é opcional
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
