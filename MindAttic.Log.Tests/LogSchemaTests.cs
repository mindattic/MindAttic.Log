using MindAttic.Log.Schema;
using NUnit.Framework;

namespace MindAttic.Log.Tests;

public class LogSchemaTests
{
    [Test]
    public void TableName_Is_MindAttic_Log() => Assert.That(LogSchema.TableName, Is.EqualTo("MindAttic_Log"));

    [Test]
    public void Sqlite_And_SqlServer_Ddl_Reference_Same_Table() =>
        Assert.Multiple(() =>
        {
            Assert.That(LogSchema.CreateTableSqlite, Does.Contain(LogSchema.TableName));
            Assert.That(LogSchema.CreateTableSqlServer, Does.Contain(LogSchema.TableName));
        });

    [Test]
    public void Columns_Cover_Every_Column_In_Sqlite_Ddl()
    {
        foreach (var column in LogSchema.Columns)
            Assert.That(LogSchema.CreateTableSqlite, Does.Contain(column), $"Sqlite DDL is missing column '{column}'.");
    }
}
