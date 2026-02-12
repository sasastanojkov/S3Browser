using DuckDB.NET.Data;

namespace S3Browser.Interfaces
{
    /// <summary>
    /// Interface for viewer windows that can execute custom SQL queries.
    /// </summary>
    public interface IQueryExecutor
    {
        /// <summary>
        /// Executes a new custom query in the window, replacing the current results.
        /// </summary>
        /// <param name="newQuery">The SQL query to execute.</param>
        void ExecuteNewQuery(string newQuery);

        /// <summary>
        /// Gets the DuckDB connection used by this viewer window.
        /// Used for executing metadata queries in the same context.
        /// </summary>
        /// <returns>The active DuckDB connection, or null if not available.</returns>
        DuckDBConnection? GetDuckDbConnection();

        /// <summary>
        /// Gets the bucket name associated with this viewer window.
        /// </summary>
        /// <returns>The S3 bucket name.</returns>
        string GetBucketName();

        /// <summary>
        /// Gets context information about the current file/folder being viewed.
        /// </summary>
        /// <returns>Display name or path for context.</returns>
        string GetContextInfo();

        /// <summary>
        /// Gets the last executed query that produced the current results.
        /// </summary>
        /// <returns>The last executed SQL query.</returns>
        string? GetLastExecutedQuery();
    }
}
