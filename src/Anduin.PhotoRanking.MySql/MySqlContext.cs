using Anduin.PhotoRanking.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anduin.PhotoRanking.MySql;

public class MySqlContext(DbContextOptions<MySqlContext> options) : TemplateDbContext(options);
