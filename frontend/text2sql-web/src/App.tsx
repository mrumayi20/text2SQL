import { useState } from "react";
import "./App.css";

export default function App() {
  const [prompt, setPrompt] = useState("");
  const [sql, setSql] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  async function generateSql() {
    setLoading(true);
    setError("");
    setSql("");

    try {
      const res = await fetch("http://localhost:5000/api/generate-sql", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ prompt }),
      });

      if (!res.ok) {
        const msg = await res.text();
        throw new Error(msg || `Request failed: ${res.status}`);
      }

      const data = (await res.json()) as { sql: string };
      setSql(data.sql);
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
    </div>
  );
}
