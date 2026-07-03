using Microsoft.EntityFrameworkCore;

namespace NotaFiscalHub.BuildingBlocks.Persistence;

public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options);
