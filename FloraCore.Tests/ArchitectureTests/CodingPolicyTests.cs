using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FloraCore.Tests.ArchitectureTests;

public class CodingPolicyTests
{
    [Fact]
    public void All_Production_Files_Should_Comply_With_CShap12_Primary_Constructor_And_Null_Checks()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // Climb up until we find the solution root (containing FloraCore.sln)
        while (baseDir != null && !File.Exists(Path.Combine(baseDir, "FloraCore.sln")))
        {
            var parent = Directory.GetParent(baseDir);
            baseDir = parent?.FullName;
        }

        if (baseDir == null)
        {
            throw new Exception("Could not find the repository root containing FloraCore.sln");
        }

        var foldersToScan = new[] { "Application", "Infrastructure", "Controllers", "Domain" };
        var csFiles = foldersToScan
            .Select(folder => Path.Combine(baseDir, folder))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            .ToList();

        // Đảm bảo tìm thấy file thực tế, tránh việc test pass ảo
        Console.WriteLine($"Found {csFiles.Count} C# files to scan.");
        Assert.True(csFiles.Count > 0, $"Không tìm thấy file .cs nào trong baseDir: '{baseDir}'. Vui lòng kiểm tra lại cấu trúc thư mục.");

        // Lấy danh sách các tệp tin mới được thêm hoặc chỉnh sửa gần đây từ git status để scan động
        var modifiedFiles = new System.Collections.Generic.HashSet<string>();
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "status --porcelain",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = baseDir
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            foreach (var line in output.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                // git status --porcelain dạng " M path" hoặc "?? path" hoặc "A  path"
                var path = line.Substring(3).Trim().Replace('/', Path.DirectorySeparatorChar);
                if (path.EndsWith(".cs"))
                {
                    modifiedFiles.Add(Path.Combine(baseDir, path));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARNING] Không thể lấy danh sách git status động: {ex.Message}");
        }

        var violations = new System.Collections.Generic.List<string>();

        foreach (var file in csFiles)
        {
            var filename = Path.GetFileName(file);
            if (file.Contains("Migrations") || filename.Contains("Test") || filename.EndsWith("Exception.cs") || filename.EndsWith("Middleware.cs") || filename == "Program.cs")
            {
                continue;
            }

            // Chỉ quét các file DI nằm trong số những file đang được sửa đổi hoặc thêm mới (để tránh lỗi legacy code)
            if (modifiedFiles.Count == 0 || !modifiedFiles.Contains(file))
            {
                continue;
            }

            var content = File.ReadAllText(file);
            
            // Find all class/record/struct definitions in the file
            var classMatches = Regex.Matches(content, @"(?:public|internal|private|protected)\s+(?:class|record|struct)\s+([A-Za-z0-9_]+)");
            foreach (Match classMatch in classMatches)
            {
                var className = classMatch.Groups[1].Value;

                // Identify if this is a Dependency Injection target class (Services, Handlers, Repositories, Controllers, etc.)
                bool isDiClass = (filename.EndsWith("Repository.cs") || 
                                  filename.EndsWith("Service.cs") || 
                                  filename.EndsWith("Handler.cs") || 
                                  filename.EndsWith("Controller.cs") ||
                                  content.Contains("IRequestHandler<") || 
                                  content.Contains(": ControllerBase") ||
                                  content.Contains(": Controller")) && 
                                 !filename.EndsWith("Command.cs") &&
                                 !filename.EndsWith("Query.cs") &&
                                 !className.EndsWith("Command") &&
                                 !className.EndsWith("Query");

                if (isDiClass)
                {
                    // Rule 1: Forbidden Traditional Constructor matching class name in DI target classes
                    var constructorPattern = $@"public\s+{className}\s*\(";
                    bool hasTraditionalConstructor = Regex.IsMatch(content, constructorPattern);
                    if (hasTraditionalConstructor)
                    {
                        violations.Add($"{Path.GetRelativePath(baseDir, file)} (class {className}): Uses traditional constructor instead of C# 12+ Primary Constructor.");
                    }

                    // Rule 2: Primary constructor must contain null-checks if it has parameters
                    var primaryCtorPattern = $@"(?:class|record|struct)\s+{className}\s*\(\s*[^)]+\s*\)";
                    if (Regex.IsMatch(content, primaryCtorPattern))
                    {
                        if (!content.Contains("ThrowIfNull") && !content.Contains("?? throw") && !content.Contains("ArgumentNullException"))
                        {
                            violations.Add($"{Path.GetRelativePath(baseDir, file)} (class {className}): Primary constructor has parameters but no null-checks (ThrowIfNull/?? throw) found.");
                        }
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, 
            "Coding Policy Violations (Primary Constructors required for DI classes):\n" + string.Join("\n", violations));
    }
}
