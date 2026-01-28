using SmartVoiceAgent.Infrastructure.Security;

namespace SmartVoiceAgent.Tests;

/// <summary>
/// Unit tests for SecurityUtilities
/// </summary>
public class SecurityUtilitiesTests
{
    #region Path Traversal Tests

    [Theory]
    [InlineData("test.txt", true)]
    [InlineData("folder/file.txt", true)]
    [InlineData("../test.txt", false)]
    [InlineData("..\\test.txt", false)]
    [InlineData("folder/../test.txt", false)]
    [InlineData("folder/..\\test.txt", false)]
    [InlineData("//server/share", false)]
    [InlineData("\\\\server\\share", false)]
    public void IsSafeFilePath_DetectsTraversalAttempts(string path, bool expectedSafe)
    {
        // Act
        bool isSafe = SecurityUtilities.IsSafeFilePath(path);

        // Assert
        Assert.Equal(expectedSafe, isSafe);
    }

    [Fact]
    public void IsSafeFilePath_WithBaseDirectory_AllowsSubdirectory()
    {
        // Arrange
        string baseDir = @"C:\Users\Test\Documents";
        string safePath = @"C:\Users\Test\Documents\file.txt";

        // Act
        bool isSafe = SecurityUtilities.IsSafeFilePath(safePath, baseDir);

        // Assert
        Assert.True(isSafe);
    }

    [Fact]
    public void IsSafeFilePath_WithBaseDirectory_BlocksOutsideDirectory()
    {
        // Arrange
        string baseDir = @"C:\Users\Test\Documents";
        string unsafePath = @"C:\Windows\System32\file.txt";

        // Act
        bool isSafe = SecurityUtilities.IsSafeFilePath(unsafePath, baseDir);

        // Assert
        Assert.False(isSafe);
    }

    [Fact]
    public void IsSafeFilePath_EmptyPath_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(SecurityUtilities.IsSafeFilePath(""));
        Assert.False(SecurityUtilities.IsSafeFilePath(null!));
        Assert.False(SecurityUtilities.IsSafeFilePath("   "));
    }

    #endregion

    #region Application Name Validation Tests

    [Theory]
    [InlineData("chrome", true)]
    [InlineData("notepad", true)]
    [InlineData("Spotify", true)]
    [InlineData("Visual Studio Code", true)]
    [InlineData("app-1.0", true)]
    [InlineData("chrome;calc", false)]
    [InlineData("chrome|notepad", false)]
    [InlineData("chrome && notepad", false)]
    [InlineData("chrome || notepad", false)]
    [InlineData("chrome`whoami`", false)]
    [InlineData("chrome$(whoami)", false)]
    [InlineData("chrome>file.txt", false)]
    [InlineData("chrome<script>", false)]
    public void IsSafeApplicationName_DetectsInjectionAttempts(string appName, bool expectedSafe)
    {
        // Act
        bool isSafe = SecurityUtilities.IsSafeApplicationName(appName);

        // Assert
        Assert.Equal(expectedSafe, isSafe);
    }

    [Fact]
    public void IsSafeApplicationName_EmptyName_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(SecurityUtilities.IsSafeApplicationName(""));
        Assert.False(SecurityUtilities.IsSafeApplicationName(null!));
    }

    [Fact]
    public void IsSafeApplicationName_LongName_ReturnsFalse()
    {
        // Arrange
        string longName = new string('a', 101);

        // Act
        bool isSafe = SecurityUtilities.IsSafeApplicationName(longName);

        // Assert
        Assert.False(isSafe);
    }

    #endregion

    #region URL Validation Tests

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://localhost:5000", true)]
    [InlineData("https://api.openrouter.ai/v1", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("javascript%3aalert(1)", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("vbscript:msgbox(1)", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("ftp://server.com", false)]
    public void IsSafeUrl_BlocksDangerousProtocols(string url, bool expectedSafe)
    {
        // Act
        bool isSafe = SecurityUtilities.IsSafeUrl(url);

        // Assert
        Assert.Equal(expectedSafe, isSafe);
    }

    [Fact]
    public void IsSafeUrl_EmptyUrl_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(SecurityUtilities.IsSafeUrl(""));
        Assert.False(SecurityUtilities.IsSafeUrl(null!));
    }

    [Fact]
    public void IsSafeUrl_ControlCharacters_ReturnsFalse()
    {
        // Arrange
        string urlWithControlChar = "https://example.com\x00test";

        // Act
        bool isSafe = SecurityUtilities.IsSafeUrl(urlWithControlChar);

        // Assert
        Assert.False(isSafe);
    }

    #endregion

    #region Sensitive Data Masking Tests

    [Theory]
    [InlineData("sk-1234567890abcdef", 4, "sk-1***********cdef")]  // 19 chars - 8 visible = 11 stars
    [InlineData("secret-key-value", 3, "sec**********lue")]      // 16 chars - 6 visible = 10 stars
    public void MaskSensitiveData_MasksMiddleCharacters(string input, int visibleChars, string expected)
    {
        // Act
        string masked = SecurityUtilities.MaskSensitiveData(input, visibleChars);

        // Assert
        Assert.Equal(expected, masked);
    }

    [Fact]
    public void MaskSensitiveData_ShortInput_ReturnsMasked()
    {
        // Act
        string masked = SecurityUtilities.MaskSensitiveData("short", 4);

        // Assert
        Assert.Equal("***", masked);
    }

    [Fact]
    public void MaskSensitiveData_NullInput_ReturnsMasked()
    {
        // Act
        string masked = SecurityUtilities.MaskSensitiveData(null!);

        // Assert
        Assert.Equal("***", masked);
    }

    #endregion

    #region Log Sanitization Tests

    [Fact]
    public void SanitizeForLog_RemovesNewlines()
    {
        // Arrange
        string input = "Line1\nLine2\r\nLine3";

        // Act
        string sanitized = SecurityUtilities.SanitizeForLog(input);

        // Assert
        Assert.DoesNotContain("\n", sanitized);
        Assert.DoesNotContain("\r", sanitized);
    }

    [Fact]
    public void SanitizeForLog_LimitsLength()
    {
        // Arrange
        string longInput = new string('a', 2000);

        // Act
        string sanitized = SecurityUtilities.SanitizeForLog(longInput, maxLength: 100);

        // Assert
        Assert.True(sanitized.Length <= 103); // 100 + "..."
        Assert.EndsWith("...", sanitized);
    }

    #endregion

    #region Dangerous Extension Tests

    [Theory]
    [InlineData(".exe", true)]
    [InlineData(".bat", true)]
    [InlineData(".cmd", true)]
    [InlineData(".sh", true)]
    [InlineData(".ps1", true)]
    [InlineData(".txt", false)]
    [InlineData(".pdf", false)]
    [InlineData(".docx", false)]
    public void IsDangerousExtension_DetectsExecutableExtensions(string extension, bool expectedDangerous)
    {
        // Act
        bool isDangerous = SecurityUtilities.IsDangerousExtension(extension);

        // Assert
        Assert.Equal(expectedDangerous, isDangerous);
    }

    [Fact]
    public void IsDangerousExtension_CaseInsensitive()
    {
        // Act
        bool isDangerous = SecurityUtilities.IsDangerousExtension(".EXE");

        // Assert
        Assert.True(isDangerous);
    }

    #endregion
}
