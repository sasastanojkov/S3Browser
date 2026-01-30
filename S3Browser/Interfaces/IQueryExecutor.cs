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
    }
}
