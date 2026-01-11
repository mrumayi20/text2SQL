import { useState } from "react";
import "./App.css";

export default function App() {
  const [prompt, setPrompt] = useState("");
  const [sql, setSql] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [columns, setColumns] = useState<string[]>([]);
  const [rows, setRows] = useState<any[][]>([]);

  async function generateSql() {
    setLoading(true);
    setError("");
    setSql("");
    setColumns([]);
    setRows([]);

    try {
      const API_BASE =
        import.meta.env.VITE_API_BASE_URL || "http://localhost:5000";

      const res = await fetch(`${API_BASE}/api/generate-and-run`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ prompt }),
      });

      if (!res.ok) {
        const msg = await res.text();
        throw new Error(msg || `Request failed: ${res.status}`);
      }

      const data = (await res.json()) as {
        sql: string;
        columns: string[];
        rows: any[][];
      };

      setSql(data.sql);
      setColumns(data.columns);
      setRows(data.rows);
    } catch (e: any) {
      setError(e?.message ?? "Something went wrong");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div style={{ maxWidth: 900, margin: "40px auto", padding: 16 }}>
      <h1>Text → T-SQL (SQL Server)</h1>

      <label style={{ display: "block", marginTop: 16, fontWeight: 600 }}>
        Describe your query
      </label>
      <textarea
        value={prompt}
        onChange={(e) => setPrompt(e.target.value)}
        rows={6}
        style={{ width: "100%", marginTop: 8, padding: 12, fontSize: 16 }}
        placeholder='e.g. "Show top 10 customers by total order amount in 2025"'
      />

      <button
        onClick={generateSql}
        disabled={!prompt.trim() || loading}
        style={{ marginTop: 12, padding: "10px 14px", fontSize: 16 }}
      >
        {loading ? "Generating..." : "Generate SQL"}
      </button>

      {error && (
        <p style={{ marginTop: 12, color: "crimson" }}>
          <b>Error:</b> {error}
        </p>
      )}

      <label style={{ display: "block", marginTop: 24, fontWeight: 600 }}>
        Generated T-SQL
      </label>
      <pre
        style={{
          whiteSpace: "pre-wrap",
          background: "#111",
          color: "#eee",
          padding: 16,
          borderRadius: 8,
          marginTop: 8,
          minHeight: 120,
        }}
      >
        {sql || "—"}
      </pre>
      {columns.length > 0 && (
        <div style={{ marginTop: 20 }}>
          <h2>Results</h2>
          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  {columns.map((c) => (
                    <th
                      key={c}
                      style={{
                        textAlign: "left",
                        borderBottom: "1px solid #ccc",
                        padding: "8px",
                      }}
                    >
                      {c}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((r, idx) => (
                  <tr key={idx}>
                    {r.map((cell, j) => (
                      <td
                        key={j}
                        style={{
                          borderBottom: "1px solid #eee",
                          padding: "8px",
                        }}
                      >
                        {cell === null || cell === undefined
                          ? ""
                          : String(cell)}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
