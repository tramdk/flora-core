using FloraCore.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using UUIDNext;
using FloraCore.Application.Common.Constants;

namespace FloraCore.Infrastructure.Data;

/// <summary>
/// Database seeding service to initialize default data.
/// Separates seeding logic from Program.cs for better maintainability.
/// </summary>
public class DatabaseSeeder(
    AppDbContext context,
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<AppUser> userManager)
{
    private readonly AppDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly RoleManager<IdentityRole<Guid>> _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
    private readonly UserManager<AppUser> _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));

    /// <summary>
    /// Ensure the database schema matches the model (Create tables).
    /// </summary>
    public async Task EnsureCreatedAsync()
    {
        // Apply migrations to ensure database schema is up to date
        // This is preferred over EnsureCreatedAsync as it supports schema evolution
        await _context.Database.MigrateAsync();
    }

    /// <summary>
    /// Seed default data.
    /// </summary>
    public async Task SeedAsync()
    {
        try
        {
            await SeedRolesAsync();
            var adminUser = await SeedAdminUserAsync();
            await SeedProductCategoriesAndProductsAsync();
            await SeedPostCategoriesAsync();
            await SeedPostsAsync(adminUser);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "An error occurred while seeding the database.");
        }
    }

    private async Task SeedRolesAsync()
    {
        if (!await _roleManager.RoleExistsAsync(RoleConstants.Admin))
        {
            await _roleManager.CreateAsync(new IdentityRole<Guid>(RoleConstants.Admin));
        }

        if (!await _roleManager.RoleExistsAsync(RoleConstants.User))
        {
            await _roleManager.CreateAsync(new IdentityRole<Guid>(RoleConstants.User));
        }
    }

    private async Task<AppUser> SeedAdminUserAsync()
    {
        var adminEmail = "admin@floracore.com";
        var adminUser = await _userManager.FindByEmailAsync(adminEmail);

        if (adminUser == null)
        {
            adminUser = new AppUser
            {
                Id = Uuid.NewDatabaseFriendly(Database.PostgreSql),
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "System Admin",
                EmailConfirmed = true
            };
            await _userManager.CreateAsync(adminUser, "Admin123!");
            await _userManager.AddToRoleAsync(adminUser, RoleConstants.Admin);
        }

        return adminUser;
    }

    private async Task SeedProductCategoriesAndProductsAsync()
    {
        if (_context.ProductCategories.Any())
        {
            // Categories exist, only check products
            if (!_context.Products.Any())
            {
                await SeedProductsWithoutCategoryAsync();
            }
            return;
        }

        var bouquetCategory = new ProductCategory
        {
            Id = Uuid.NewDatabaseFriendly(Database.PostgreSql),
            Name = "Hoa Bó",
            Description = "Các mẫu hoa bó nghệ thuật, sang trọng",
            CreatedAt = DateTime.UtcNow
        };

        var basketCategory = new ProductCategory
        {
            Id = Uuid.NewDatabaseFriendly(Database.PostgreSql),
            Name = "Hoa Giỏ",
            Description = "Giỏ hoa trang trí, quà tặng tinh tế",
            CreatedAt = DateTime.UtcNow
        };

        _context.ProductCategories.AddRange(bouquetCategory, basketCategory);
        await _context.SaveChangesAsync();

        if (!_context.Products.Any())
        {
            _context.Products.AddRange(new List<Product>
            {
                new()
                {
                    Id = Uuid.NewDatabaseFriendly(Database.PostgreSql),
                    Name = "Bó Hoa Hồng Red Naomi",
                    Description = "Sự kết hợp hoàn hảo giữa hoa hồng đỏ và lá phụ đi kèm",
                    Price = 450000m,
                    Stock = 10,
                    CreatedAt = DateTime.UtcNow,
                    CategoryId = bouquetCategory.Id
                },
                new()
                {
                    Id = Uuid.NewDatabaseFriendly(Database.PostgreSql),
                    Name = "Giỏ Hoa Hướng Dương Nắng Mai",
                    Description = "Mang lại năng lượng tích cực và niềm vui cho ngày mới",
                    Price = 550000m,
                    Stock = 5,
                    CreatedAt = DateTime.UtcNow,
                    CategoryId = basketCategory.Id
                },
                new()
                {
                    Id = Uuid.NewDatabaseFriendly(Database.PostgreSql),
                    Name = "Bó Hoa Cúc Tự Nhiên",
                    Description = "Vẻ đẹp đơn giản nhưng không kém phần thanh lịch",
                    Price = 250000m,
                    Stock = 20,
                    CreatedAt = DateTime.UtcNow,
                    CategoryId = bouquetCategory.Id
                }
            });
            await _context.SaveChangesAsync();
        }
    }

    private async Task SeedProductsWithoutCategoryAsync()
    {
        _context.Products.AddRange(new List<Product>
        {
            new()
            {
                Id = Uuid.NewDatabaseFriendly(Database.PostgreSql),
                Name = "Hoa Hồng Đà Lạt (Bó mẫu)",
                Description = "Sản phẩm thử nghiệm không thuộc danh mục",
                Price = 300000m,
                Stock = 10,
                CreatedAt = DateTime.UtcNow
            }
        });
        await _context.SaveChangesAsync();
    }

    private async Task SeedPostCategoriesAsync()
    {
        if (_context.PostCategories.Any()) return;

        _context.PostCategories.AddRange(
            new PostCategory
            {
                Id = "flower-care",
                Name = "Chăm sóc hoa",
                CreatedAt = DateTime.UtcNow
            },
            new PostCategory
            {
                Id = "flower-meaning",
                Name = "Ý nghĩa hoa",
                CreatedAt = DateTime.UtcNow
            },
            new PostCategory
            {
                Id = "wedding-trends",
                Name = "Xu hướng",
                CreatedAt = DateTime.UtcNow
            },
            new PostCategory
            {
                Id = "blog",
                Name = "Blog",
                CreatedAt = DateTime.UtcNow
            },
            new PostCategory
            {
                Id = "feedback",
                Name = "Feedback",
                CreatedAt = DateTime.UtcNow
            },
            new PostCategory
            {
                Id = "intro",
                Name = "Giới thiệu",
                CreatedAt = DateTime.UtcNow
            }
        );
        await _context.SaveChangesAsync();
    }

    private async Task SeedPostsAsync(AppUser adminUser)
    {
        if (_context.Posts.Any()) return;

        _context.Posts.AddRange(new List<Post>
        {
            new()
            {
                Id = Uuid.NewDatabaseFriendly(Database.PostgreSql),
                Title = "Cách giữ hoa tươi lâu tại nhà",
                Content = "Để hoa tươi lâu, bạn cần thay nước mỗi ngày và cắt gốc hoa theo chiều chéo để hút nước tốt hơn...",
                AuthorId = adminUser.Id,
                CreatedAt = DateTime.UtcNow,
                CategoryId = "blog"
            },
            new()
            {
                Id = Uuid.NewDatabaseFriendly(Database.PostgreSql),
                Title = "Ý nghĩa của các loài hoa trong tình yêu",
                Content = "Hoa hồng đỏ tượng trưng cho tình yêu nồng cháy, trong khi hoa baby trắng lại mang ý nghĩa tình yêu tinh khôi...",
                AuthorId = adminUser.Id,
                CreatedAt = DateTime.UtcNow,
                CategoryId = "blog"
            },
            new()
            {
                Id = Uuid.NewDatabaseFriendly(Database.PostgreSql),
                Title = "Xu hướng chọn hoa cưới năm 2026",
                Content = "Năm 2026, các tone màu vintage và sự tối giản trong thiết kế hoa cầm tay cô dâu sẽ lên ngôi...",
                AuthorId = adminUser.Id,
                CreatedAt = DateTime.UtcNow,
                CategoryId = "intro"
            }
        });
        await _context.SaveChangesAsync();
    }
}

/// <summary>
/// Extension methods for WebApplication to seed database.
/// </summary>
public static class DatabaseSeederExtensions
{
    /// <summary>
    /// Initialize the database schema (Create Tables).
    /// </summary>
    /// <summary>
    /// Initialize the database schema (Create Tables).
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        try 
        {
            using var scope = app.Services.CreateScope();
            var seeder = ActivatorUtilities.CreateInstance<DatabaseSeeder>(scope.ServiceProvider);
            await seeder.EnsureCreatedAsync();
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P07")
        {
            // Error 42P07: relation already exists
            // This happens when tables exist but EF Migration History is missing.
            // We swallow this error to verify if the app can still run with existing schema.
            Log.Warning("Database tables already exist. Skipping Migration. (Error 42P07)");
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("already exists"))
        {
            // SQLite Error 1: table already exists
            Log.Warning("SQLite database tables already exist. Skipping Migration.");
        }
        catch (Exception ex)
        {
            // Check if inner exception is the Postgres one
            if (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "42P07")
            {
                Log.Warning("Database tables already exist. Skipping Migration. (Error 42P07)");
                return;
            }

            // Check if inner exception is the SQLite one
            if (ex.InnerException is Microsoft.Data.Sqlite.SqliteException slEx && slEx.SqliteErrorCode == 1 && slEx.Message.Contains("already exists"))
            {
                Log.Warning("SQLite database tables already exist. Skipping Migration.");
                return;
            }
            
            Log.Fatal(ex, "CRITICAL ERROR: Failed to initialize/migrate database.");
            throw;
        }
    }

    /// <summary>
    /// Seed the database with default data.
    /// </summary>
    public static async Task SeedDatabaseAsync(this WebApplication app)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var seeder = ActivatorUtilities.CreateInstance<DatabaseSeeder>(scope.ServiceProvider);
            await seeder.SeedAsync();
        }
        catch (Exception ex)
        {
             Log.Error(ex, "ERROR: Failed to seed database.");
        }
    }

    /// <summary>
    /// Synchronize revoked tokens from the database into the distributed cache.
    /// This fixes the vulnerability where in-memory cache blacklists are lost upon application restart.
    /// </summary>
    public static async Task SyncTokenBlacklistAsync(this WebApplication app)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var blacklistService = scope.ServiceProvider.GetRequiredService<FloraCore.Application.Common.Services.ITokenBlacklistService>();

            // Only fetch revoked tokens that haven't expired yet
            var activeRevokedTokens = await context.RefreshTokens
                .Where(rt => rt.IsRevoked && rt.ExpiryDate > DateTime.UtcNow)
                .ToListAsync();

            int syncCount = 0;
            foreach (var rt in activeRevokedTokens)
            {
                var expiry = rt.ExpiryDate - DateTime.UtcNow;
                if (expiry > TimeSpan.Zero && !string.IsNullOrEmpty(rt.Jti))
                {
                    await blacklistService.BlacklistTokenAsync(rt.Jti, expiry);
                    syncCount++;
                }
            }

            if (syncCount > 0)
            {
                Log.Information("Synchronized {Count} revoked tokens to blacklist cache.", syncCount);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ERROR: Failed to sync token blacklist.");
        }
    }
}
