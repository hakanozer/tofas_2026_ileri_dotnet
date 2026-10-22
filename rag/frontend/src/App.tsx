import React, { useEffect, useState, useRef } from "react";

type Message = {
  id: string;
  role: "user" | "assistant" | "system";
  text: string;
  time: number;
};

type Conversation = {
  id: string;
  title: string;
  messages: Message[];
  createdAt: number;
};

const STORAGE_KEY = "rag_chat_conversations_v1";

function uid(prefix = "id") {
  return `${prefix}_${Math.random().toString(36).slice(2, 9)}`;
}

export default function App() {
  const [conversations, setConversations] = useState<Conversation[]>(() => {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? JSON.parse(raw) : [];
    } catch (e) {
      return [];
    }
  });

  const [activeId, setActiveId] = useState<string | null>(() => {
    return conversations[0]?.id ?? null;
  });
  const [input, setInput] = useState("");
  const [mode, setMode] = useState<"chat" | "agent">("chat");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [agentSession, setAgentSession] = useState<string | null>(null);
  const [agentCount, setAgentCount] = useState(0);
  const messagesEndRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(conversations));
  }, [conversations]);

  useEffect(() => {
    if (!activeId && conversations.length) setActiveId(conversations[0].id);
  }, [conversations, activeId]);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [activeId, conversations]);

  function createConversation() {
    const c: Conversation = {
      id: uid("conv"),
      title: `Yeni Sohbet ${new Date().toLocaleString()}`,
      messages: [],
      createdAt: Date.now(),
    };
    setConversations((s) => [c, ...s]);
    setActiveId(c.id);
  }

  function deleteConversation(id: string) {
    setConversations((s) => s.filter((c) => c.id !== id));
    if (activeId === id) setActiveId((prev) => null);
  }

  function addMessageToActive(msg: Message) {
    if (!activeId) return;
    setConversations((s) =>
      s.map((c) => (c.id === activeId ? { ...c, messages: [...c.messages, msg] } : c))
    );
  }

  function updateActiveTitle(title: string) {
    if (!activeId) return;
    setConversations((s) => s.map((c) => (c.id === activeId ? { ...c, title } : c)));
  }

  function getActive(): Conversation | undefined {
    return conversations.find((c) => c.id === activeId);
  }

  async function sendChatQuery() {
    const q = input.trim();
    if (!q) return;
    setError(null);
    const userMsg: Message = { id: uid("m"), role: "user", text: q, time: Date.now() };
    addMessageToActive(userMsg);
    setInput("");
    setLoading(true);
    try {
      const res = await fetch("http://localhost:8000/api/v1/query", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ query: q, max_results: 5 }),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.json();
      const assistantMsg: Message = { id: uid("m"), role: "assistant", text: data.answer ?? JSON.stringify(data), time: Date.now() };
      addMessageToActive(assistantMsg);
      // update title with short preview
      updateActiveTitle(q.slice(0, 40));
    } catch (e: any) {
      setError(e.message ?? "Unknown error");
      const errMsg: Message = { id: uid("m"), role: "assistant", text: "Hata oluştu: " + (e.message ?? ""), time: Date.now() };
      addMessageToActive(errMsg);
    } finally {
      setLoading(false);
    }
  }

  async function startAgentSession() {
    setError(null);
    setLoading(true);
    try {
      const res = await fetch("http://localhost:8000/api/v1/agent/session", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({}) });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.json();
      setAgentSession(data.session_id);
      setAgentCount(0);
      const sysMsg: Message = { id: uid("m"), role: "system", text: data.message ?? "Oturum başladı.", time: Date.now() };
      addMessageToActive(sysMsg);
    } catch (e: any) {
      setError(e.message ?? "Unknown error");
    } finally {
      setLoading(false);
    }
  }

  async function sendAgentQuery() {
    const q = input.trim();
    if (!q) return;
    if (!agentSession) {
      setError("Agent oturumu yok. Önce oturum başlatın.");
      return;
    }
    if (agentCount >= 5) {
      setError("Agent ile en fazla 5 ardışık soru sorabilirsiniz. Yeni oturum başlatın.");
      return;
    }
    setError(null);
    const userMsg: Message = { id: uid("m"), role: "user", text: q, time: Date.now() };
    addMessageToActive(userMsg);
    setInput("");
    setLoading(true);
    try {
      const res = await fetch("http://localhost:8000/api/v1/agent/query", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ session_id: agentSession, query: q }),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.json();
      // If backend returned structured agent trace, format it for display
      let text = data.answer ?? "";
      if (data.intent) text = `Intent: ${data.intent}\n\n${text}`;
      if (data.tools_used) text += `\n\nTools used: ${JSON.stringify(data.tools_used)}`;
      if (data.step_count) text += `\nStep count: ${data.step_count}`;
      if (Array.isArray(data.steps) && data.steps.length) {
        text += `\n\nSteps:\n`;
        data.steps.forEach((s: any, i: number) => {
          const tool = s.tool ?? s["tool"] ?? "";
          const input = s.input ?? s["input"] ?? "";
          const output = s.output ?? s["output"] ?? "";
          text += `${i + 1}. [${tool}] input: ${typeof input === 'string' ? input : JSON.stringify(input)}\n   output: ${typeof output === 'string' ? output : JSON.stringify(output)}\n`;
        });
      }
      const assistantMsg: Message = { id: uid("m"), role: "assistant", text: text || JSON.stringify(data), time: Date.now() };
      addMessageToActive(assistantMsg);
      setAgentCount((c) => c + 1);
      if (data.intent) updateActiveTitle(`${data.intent} • ${q.slice(0,30)}`);
    } catch (e: any) {
      setError(e.message ?? "Unknown error");
      const errMsg: Message = { id: uid("m"), role: "assistant", text: "Hata oluştu: " + (e.message ?? ""), time: Date.now() };
      addMessageToActive(errMsg);
    } finally {
      setLoading(false);
    }
  }

  async function closeAgentSession() {
    if (!agentSession) return;
    setLoading(true);
    setError(null);
    try {
      const res = await fetch(`http://localhost:8000/api/v1/agent/session/${agentSession}`, { method: "DELETE" });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.json();
      const sysMsg: Message = { id: uid("m"), role: "system", text: data.message ?? "Oturum kapatıldı.", time: Date.now() };
      addMessageToActive(sysMsg);
      setAgentSession(null);
      setAgentCount(0);
    } catch (e: any) {
      setError(e.message ?? "Unknown error");
    } finally {
      setLoading(false);
    }
  }

  function handleSend() {
    if (mode === "chat") sendChatQuery();
    else sendAgentQuery();
  }

  return (
    <div className="container-fluid vh-100 d-flex p-0">
      <div className="row g-0 w-100">
        <aside className="col-12 col-md-4 col-lg-3 border-end bg-light d-flex flex-column" style={{maxHeight: '100vh', overflow: 'auto'}}>
          <div className="d-flex align-items-center p-3 border-bottom">
            <h5 className="mb-0">Sohbetler</h5>
            <button className="btn btn-sm btn-primary ms-auto" onClick={createConversation}>Yeni</button>
          </div>
          <div className="list-group list-group-flush">
            {conversations.map((c) => (
              <div key={c.id} className={`list-group-item list-group-item-action d-flex align-items-start ${c.id===activeId? 'active':''}`} onClick={() => setActiveId(c.id)} style={{cursor:'pointer'}}>
                <div className="flex-grow-1">
                  <div className="d-flex align-items-center">
                    <strong className="me-2" style={{fontSize: '0.95rem'}}>{c.title}</strong>
                    <small className="text-muted ms-auto">{new Date(c.createdAt).toLocaleTimeString()}</small>
                  </div>
                  <div className="text-truncate" style={{maxWidth: '240px'}}>
                    {c.messages[c.messages.length-1]?.text ?? "Henüz mesaj yok"}
                  </div>
                </div>
                <button className="btn btn-sm btn-outline-danger ms-2" onClick={(e)=>{e.stopPropagation(); deleteConversation(c.id);}}>Sil</button>
              </div>
            ))}
            {conversations.length===0 && <div className="p-3 text-muted">Henüz sohbet yok. Yeni sohbet başlatın.</div>}
          </div>
        </aside>

        <main className="col-12 col-md-8 col-lg-9 d-flex flex-column" style={{height: '100vh'}}>
          <header className="d-flex align-items-center p-3 border-bottom">
            <div className="btn-group" role="group">
              <button className={`btn btn-sm ${mode==='chat'? 'btn-primary':'btn-outline-primary'}`} onClick={()=>setMode('chat')}>Chat (RAG)</button>
              <button className={`btn btn-sm ${mode==='agent'? 'btn-primary':'btn-outline-primary'}`} onClick={()=>setMode('agent')}>Agent</button>
            </div>
            <div className="ms-3 text-muted">Mode: {mode.toUpperCase()}</div>
            <div className="ms-auto d-flex align-items-center">
              {mode==='agent' && (
                <>
                  <button className="btn btn-sm btn-outline-success me-2" onClick={startAgentSession} disabled={!!agentSession}>Oturum Başlat</button>
                  <button className="btn btn-sm btn-outline-secondary me-2" onClick={closeAgentSession} disabled={!agentSession}>Oturumu Kapat</button>
                  <div className="me-2">Session: {agentSession ?? '-'} </div>
                </>
              )}
              <div className="text-danger ms-2">{error}</div>
            </div>
          </header>

          <section className="flex-grow-1 p-3 overflow-auto" style={{background:'#f7f9fc'}}>
            {activeId ? (
              <div className="d-flex flex-column h-100">
                <div className="card flex-grow-1 mb-3">
                  <div className="card-body d-flex flex-column" style={{minHeight:0}}>
                    <div className="flex-grow-1 overflow-auto mb-3">
                      {getActive()?.messages.map((m) => (
                        <div key={m.id} className={`mb-3 d-flex ${m.role==='user'? 'justify-content-end':''}`}>
                          <div className={`p-2 rounded ${m.role==='user'? 'bg-primary text-white':'bg-white border'}`} style={{maxWidth:'80%'}}>
                            <div style={{whiteSpace:'pre-wrap'}}>{m.text}</div>
                            <div className="text-muted small mt-1">{new Date(m.time).toLocaleTimeString()}</div>
                          </div>
                        </div>
                      ))}
                      <div ref={messagesEndRef} />
                    </div>
                  </div>
                </div>

                <div className="d-flex align-items-center">
                  <input className="form-control me-2" placeholder={mode==='chat'? 'Soru yazın (RAG sorgusu)':'Agent ile konuşun (önce oturum başlatın)'} value={input} onChange={(e)=>setInput(e.target.value)} onKeyDown={(e)=>{ if(e.key==='Enter'){ handleSend(); }}} />
                  <button className="btn btn-success" onClick={handleSend} disabled={loading}>{loading? '...' : 'Gönder'}</button>
                </div>

                <div className="mt-2 d-flex justify-content-between small text-muted">
                  <div>Agent soru sayısı: {agentCount}/5</div>
                  <div>LocalStorage: {conversations.length} sohbet</div>
                </div>
              </div>
            ) : (
              <div className="p-4 text-center text-muted">Lütfen sol taraftan bir sohbet seçin ya da yeni oluşturun.</div>
            )}
          </section>
        </main>
      </div>
    </div>
  );
}
