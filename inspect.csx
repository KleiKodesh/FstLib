#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.Data.Sqlite, 10.0.8"

using System;
using System.IO;
using Microsoft.Data.Sqlite;

string dbPath = Path.Combine(AppContext.BaseDirectory, "FstTest", "SqliteIndex", "seg_0_31.db");

if (!File.Exists(dbPath))
{
    Console.WriteLine($"Database not found: {dbPath}");
    return;
}

var connectionString = $"Data Source={dbPath}";
using (var connection = new SqliteConnection(connectionString))
{
    connection.Open();

    // List tables
    Console.WriteLine("Tables in database:");
    using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                string tableName = reader.GetString(0);
                Console.WriteLine($"  - {tableName}");
            }
        }
    }
}
