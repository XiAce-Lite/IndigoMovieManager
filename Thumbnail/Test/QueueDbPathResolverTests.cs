using System;
using System.IO;
using System.Data.SQLite;
using NUnit.Framework;


namespace IndigoMovieManager.Thumbnail.Test
{
    [TestFixture]
    public class QueueDbPathResolverTests
    {
        [Test]
        public void ResolvePath_GeneratesCorrectPath_WithHash()
        {
            // Arrange
            string mainDbPath = @"D:\Movies\Anime2026.bw";
            string expectedFileNamePrefix = "Anime2026";
            
            // Act
            string resolvedPath = QueueDbPathResolver.ResolvePath(mainDbPath);

            // Assert
            Assert.That(resolvedPath, Does.StartWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
            Assert.That(resolvedPath, Does.Contain(@"IndigoMovieManager_fork_workthree\QueueDb"));
            
            string fileName = Path.GetFileName(resolvedPath);
            Assert.That(fileName, Does.StartWith(expectedFileNamePrefix + "."));
            Assert.That(fileName, Does.EndWith(".queue.imm"));
            
            // ハッシュ部分（8文字）が含まれているか簡易チェック
            string[] parts = fileName.Split('.');
            Assert.That(parts.Length, Is.EqualTo(4)); // Anime2026, [HASH], queue, imm
            Assert.That(parts[1].Length, Is.EqualTo(8));
        }
    }
}
