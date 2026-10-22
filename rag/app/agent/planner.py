"""
AI Agent Planner — ReAct döngüsü ile çalışan ana agent.
Intent tabanlı tool seçimi + session yönetimi.
"""
from __future__ import annotations

import time
from dataclasses import dataclass, field, asdict
from typing import Dict, List

import structlog
from langchain.agents import AgentExecutor, create_react_agent
from langchain.tools import BaseTool
from langchain_community.llms import Ollama
from langchain_core.prompts import PromptTemplate
from langchain.memory import ConversationBufferWindowMemory

from app.agent.tools import ALL_TOOLS
from app.core.cache import cache
from app.core.config import settings

logger = structlog.get_logger(__name__)

# ---------------------------------------------------------------------------
# Intent → Tool eşlemesi
# Kullanıcı sorusuna göre sadece ilgili tool'lar seçilir.
# Böylece LLM daha az seçenekle karışmaz, doğru tool'u çağırır.
# ---------------------------------------------------------------------------

INTENT_TOOL_MAP: dict[str, list[str]] = {
    "search":  ["search_products"],
    "price":   ["search_products", "check_price"],
    "stock":   ["search_products", "check_stock"],
    "compare": ["search_products", "compare_products"],
    "pdf":     ["print_pdf"],
    "default": ["search_products", "check_price", "check_stock", "compare_products"],
}

# Anahtar kelime → intent eşlemesi
INTENT_KEYWORDS: dict[str, list[str]] = {
    "price":   ["fiyat", "tl", "lira", "kaç para", "ücret", "maliyet", "altı", "üstü"],
    "stock":   ["stok", "mevcut", "var mı", "kaldı mı", "adet"],
    "compare": ["karşılaştır", "fark", "hangisi", "vs", "versus", "arasındaki"],
    "pdf":     ["pdf", "yazdır", "indir", "dışa aktar", "export"],
}


def detect_intent(query: str) -> str:
    """Kullanıcı sorgusundan intent tespit eder."""
    q = query.lower()
    for intent, keywords in INTENT_KEYWORDS.items():
        if any(kw in q for kw in keywords):
            return intent
    return "search"  # varsayılan


def select_tools(query: str) -> List[BaseTool]:
    """Intent'e göre ilgili tool'ları döner."""
    intent = detect_intent(query)
    tool_names = INTENT_TOOL_MAP.get(intent, INTENT_TOOL_MAP["default"])
    selected = [t for t in ALL_TOOLS if t.name in tool_names]
    logger.info("tools_selected", intent=intent, tool_names=tool_names)
    return selected


# ---------------------------------------------------------------------------
# ReAct Prompt — LangChain'in beklediği değişken adları korunuyor
# {tools}, {tool_names}, {input}, {agent_scratchpad} zorunlu.
# {chat_history} memory için eklendi.
# ---------------------------------------------------------------------------

REACT_PROMPT = PromptTemplate.from_template(
    """Sen bir e-ticaret asistanısın. Kullanıcılara Türkçe yardım edersin.

Kullanabileceğin araçlar:
{tools}

KURALLAR (kesinlikle uy):
1. Her adımda aşağıdaki formattan SAPMA. Başka hiçbir format geçerli değil.
2. "Final Answer:" satırından sonra ASLA "Action:" yazma.
3. Yeterli bilgin varsa doğrudan "Final Answer:" yaz, araç çağırma.
4. Aynı aracı aynı girdiyle ikinci kez çağırma.
5. Action satırında SADECE araç adını yaz: {tool_names}
6. Action Input satırında SADECE düz metin yaz, JSON veya = işareti kullanma.

FORMAT (her adımda bu sırayı takip et):
Thought: <ne yapacağını açıkla>
Action: <araç_adı>
Action Input: <düz metin girdi>
Observation: <araç sonucu — sen yazma, sistem yazar>
... (gerekirse tekrarla)
Thought: Yeterli bilgim var, yanıt verebilirim.
Final Answer: <kullanıcıya Türkçe yanıt>

Geçmiş konuşma:
{chat_history}

Kullanıcı sorusu: {input}

{agent_scratchpad}"""
)


# ---------------------------------------------------------------------------
# Veri sınıfları
# ---------------------------------------------------------------------------

@dataclass
class AgentResponse:
    answer: str
    steps: list[dict] = field(default_factory=list)
    tools_used: list[str] = field(default_factory=list)
    intent: str = "unknown"


@dataclass
class SessionData:
    memory: ConversationBufferWindowMemory
    created_at: float = field(default_factory=time.time)
    last_used: float = field(default_factory=time.time)


# ---------------------------------------------------------------------------
# CommerceAgent
# ---------------------------------------------------------------------------

class CommerceAgent:
    """
    E-ticaret asistanı.
    - Her session için bağımsız memory tutar.
    - Her sorgu için intent tespiti yapıp sadece ilgili tool'ları kullanır.
    - Executor her çağrıda intent'e göre yeniden kurulur (tool seti değişebilir).
    """

    SESSION_TTL = 60 * 30  # 30 dakika

    def __init__(self) -> None:
        self._sessions: Dict[str, SessionData] = {}

    def _build_llm(self) -> Ollama:
        return Ollama(
            base_url=settings.ollama_base_url,
            model=settings.ollama_model,
            temperature=0.1,
            num_predict=2048,  # 1024 → 2048: Final Answer için yeterli alan
        )

    def _build_executor(self, tools: List[BaseTool], memory: ConversationBufferWindowMemory) -> AgentExecutor:
        """Verilen tool seti ve memory ile executor oluşturur."""
        agent = create_react_agent(
            llm=self._build_llm(),
            tools=tools,
            prompt=REACT_PROMPT,
        )
        return AgentExecutor(
            agent=agent,
            tools=tools,
            memory=memory,
            verbose=True,
            max_iterations=6,
            max_execution_time=90,
            handle_parsing_errors=(
                "Format hatası. Şu şablonu kullan:\n"
                "Thought: ...\nAction: <araç_adı>\nAction Input: <metin>\n"
                "ya da bilgin yeterliyse:\n"
                "Thought: ...\nFinal Answer: <yanıt>"
            ),
            return_intermediate_steps=True,
        )

    def _get_or_create_session(self, session_id: str) -> SessionData:
        self._evict_expired_sessions()
        if session_id not in self._sessions:
            logger.info("session_created", session_id=session_id)
            self._sessions[session_id] = SessionData(
                memory=ConversationBufferWindowMemory(
                    k=5,
                    memory_key="chat_history",
                    output_key="output",      # çoklu output key uyarısını kapatır
                    return_messages=False,
                )
            )
        session = self._sessions[session_id]
        session.last_used = time.time()
        return session

    def _evict_expired_sessions(self) -> None:
        now = time.time()
        expired = [sid for sid, s in self._sessions.items() if now - s.last_used > self.SESSION_TTL]
        for sid in expired:
            logger.info("session_evicted_ttl", session_id=sid)
            del self._sessions[sid]

    def _extract_answer_from_steps(self, steps: list[dict]) -> str | None:
        """intermediate_steps içinde anlamlı veri olan son gözlemi döner."""
        for step in reversed(steps):
            out = step.get("output", "")
            # "is not a valid tool" veya boş gözlemleri atla
            if out and "not a valid tool" not in out and out.lower() != "none":
                return out
        return None

    async def run(self, session_id: str, user_input: str) -> AgentResponse:
        session = self._get_or_create_session(session_id)
        intent = detect_intent(user_input)
        tools = select_tools(user_input)

        logger.info(
            "agent_run_started",
            session_id=session_id,
            intent=intent,
            tools=[t.name for t in tools],
            query=user_input,
        )

        executor = self._build_executor(tools, session.memory)

        try:
            result = await executor.ainvoke({"input": user_input})
        except Exception as e:
            logger.exception("agent_run_failed", session_id=session_id, error=str(e))
            return AgentResponse(answer="Üzgünüm, bir hata oluştu. Lütfen tekrar deneyin.", intent=intent)

        # Adımları derle — None ve hata adımlarını filtrele
        steps = []
        tools_used = []
        for action, observation in result.get("intermediate_steps", []):
            tool_name = action.tool
            obs_str = str(observation)
            steps.append({
                "tool": tool_name,
                "input": action.tool_input,
                "output": obs_str[:300],
            })
            if tool_name not in ("None", "_Exception", "Final Answer"):
                tools_used.append(tool_name)

        # Yanıtı belirle
        answer = result.get("output", "").strip()
        STOP_MARKERS = (
            "agent stopped due to iteration limit",
            "agent stopped due to time limit",
        )
        if not answer or any(m in answer.lower() for m in STOP_MARKERS):
            logger.warning("agent_fallback_triggered", session_id=session_id)
            fallback = self._extract_answer_from_steps(steps)
            if fallback:
                answer = f"Araştırdım, bulduklarım şunlar:\n\n{fallback}"
            else:
                answer = "Üzgünüm, sorunuzu yanıtlamak için yeterli bilgiye ulaşamadım. Lütfen daha açık belirtin."

        return AgentResponse(
            answer=answer,
            steps=steps,
            tools_used=list(dict.fromkeys(tools_used)),  # sırayı koruyarak unique
            intent=intent,
        )

    def clear_session(self, session_id: str) -> bool:
        if session_id in self._sessions:
            del self._sessions[session_id]
            logger.info("session_deleted", session_id=session_id)
            return True
        return False

    def session_exists(self, session_id: str) -> bool:
        return session_id in self._sessions

    @property
    def active_session_count(self) -> int:
        return len(self._sessions)


# Singleton
commerce_agent = CommerceAgent()


REACT_PROMPT = PromptTemplate.from_template(
    """Sen AI Commerce Assistant'sın. Kullanıcılara e-ticaret sorularında yardım edersin.
Elindeki araçları kullanarak adım adım düşün ve en doğru yanıtı ver.
Türkçe konuş.

Elindeki araçlar:
{tools}

Araç adları: {tool_names}

Geçmiş konuşma:
{chat_history}

Kullanıcı sorusu: {input}

ÖNEMLİ KURALLAR:
- Action Input her zaman SADECE düz metin olmalıdır. JSON, dict veya parametre formatı KULLANMA.
- Bir araç işe yaramadıysa aynı aracı aynı inputla tekrar çağırma. Farklı bir yaklaşım dene.
- Elindeki bilgi yeterliyse hemen Final Answer yaz, gereksiz araç çağrısı yapma.
- tool_names listesinde olmayan bir araç kullanma.

Aşağıdaki formatı KULLAN:

Thought: [Ne yapmalıyım? Hangi aracı kullanmalıyım?]
Action: [araç_adı]
Action Input: [araç_girdisi]
Observation: [araç çıktısı]
... (Bu döngü tekrarlanabilir, ama gereksiz tekrar yapma)
Thought: Artık yanıt verebilecek kadar bilgim var.
Final Answer: [Kullanıcıya verilecek Türkçe yanıt]

Başla!
{agent_scratchpad}"""
)


@dataclass
class AgentResponse:
    """Agent yanıt modeli."""
    answer: str
    steps: list[dict] = field(default_factory=list)
    tools_used: list[str] = field(default_factory=list)
    intent: str = "unknown"


@dataclass
class SessionData:
    """Bir kullanıcı session'ına ait tüm durum."""
    memory: ConversationBufferWindowMemory
    executor: AgentExecutor
    created_at: float = field(default_factory=time.time)
    last_used: float = field(default_factory=time.time)


class CommerceAgent:
    """
    E-ticaret asistanı agent'ı.
    Her session için ayrı memory ve executor tutar.
    Böylece farklı kullanıcıların konuşmaları birbirinden izole kalır.
    """

    # Session TTL: 30 dakika (saniye cinsinden)
    SESSION_TTL = 60 * 30

    def __init__(self) -> None:
        # session_id -> SessionData
        self._sessions: Dict[str, SessionData] = {}

    def _build_llm(self) -> Ollama:
        return Ollama(
            base_url=settings.ollama_base_url,
            model=settings.ollama_model,
            temperature=0.1,
            num_predict=1024,
        )

    def _build_session(self) -> SessionData:
        """Yeni bir session oluşturur; her session'ın memory'si bağımsızdır."""
        memory = ConversationBufferWindowMemory(
            k=5,
            memory_key="chat_history",
            output_key="output",
            return_messages=False,
        )

        agent = create_react_agent(
            llm=self._build_llm(),
            tools=ALL_TOOLS,
            prompt=REACT_PROMPT,
        )

        executor = AgentExecutor(
            agent=agent,
            tools=ALL_TOOLS,
            memory=memory,
            verbose=True,
            max_iterations=10,                # 5 → 10: karmaşık sorgular için daha fazla alan
            max_execution_time=60,            # 60 sn güvenlik kilidi
            handle_parsing_errors=True,       # LLM format hatalarını tolere et
            return_intermediate_steps=True,
        )

        return SessionData(memory=memory, executor=executor)

    def _get_or_create_session(self, session_id: str) -> SessionData:
        """
        Var olan session'ı getirir ya da yeni bir tane oluşturur.
        Süresi dolmuş session'ları temizler.
        """
        self._evict_expired_sessions()

        if session_id not in self._sessions:
            logger.info("session_created", session_id=session_id)
            self._sessions[session_id] = self._build_session()

        session = self._sessions[session_id]
        session.last_used = time.time()
        return session

    def _evict_expired_sessions(self) -> None:
        """TTL süresi dolan session'ları bellekten kaldırır."""
        now = time.time()
        expired = [
            sid for sid, s in self._sessions.items()
            if now - s.last_used > self.SESSION_TTL
        ]
        for sid in expired:
            logger.info("session_evicted_ttl", session_id=sid)
            del self._sessions[sid]


    async def run(self, session_id: str, user_input: str) -> AgentResponse:
        """
        Belirli bir session için kullanıcı girdisini işler.

        Args:
            session_id: Kullanıcıya ait benzersiz oturum kimliği
            user_input: Kullanıcının sorusu

        Returns:
            AgentResponse: Yanıt ve kullanılan araçlar
        """

        session = self._get_or_create_session(session_id)

        logger.info("agent_run_started", session_id=session_id, query=user_input)

        # ---------------------------------------------------
        # CACHE KEY
        # ---------------------------------------------------
        cache_prefix = "agent_response"

        # Kullanıcı inputunu normalize et
        normalized_input = user_input.strip().lower()

        # ---------------------------------------------------
        # CACHE CHECK
        # ---------------------------------------------------
        try:
            cached_response = await cache.get(
                prefix=cache_prefix,
                query=normalized_input,
            )

            if cached_response:
                logger.info("agent_cache_hit", session_id=session_id, cache_prefix=cache_prefix)
                return AgentResponse(**cached_response)

        except Exception as e:
            logger.warning("agent_cache_read_failed", session_id=session_id, cache_prefix=cache_prefix, error=str(e))

        # ---------------------------------------------------
        # LLM EXECUTION
        # ---------------------------------------------------
        try:
            result = await session.executor.ainvoke(
                {"input": user_input}
            )

        except Exception as e:
            logger.exception("agent_run_failed", session_id=session_id, error=str(e))

            return AgentResponse(
                answer="Üzgünüm, isteğinizi işlerken bir hata oluştu. Lütfen tekrar deneyin.",
            )

        # ---------------------------------------------------
        # TOOL PARSE
        # ---------------------------------------------------
        tools_used = []
        steps = []

        for action, observation in result.get("intermediate_steps", []):
            tools_used.append(action.tool)

            steps.append({
                "tool": action.tool,
                "input": action.tool_input,
                "output": str(observation)[:200],
            })

        # ---------------------------------------------------
        # OUTPUT HANDLING
        # ---------------------------------------------------
        answer = result.get("output", "").strip()

        LIMIT_MARKERS = (
            "agent stopped due to iteration limit",
            "agent stopped due to time limit",
        )

        if not answer or any(m in answer.lower() for m in LIMIT_MARKERS):

            logger.warning("agent_fallback_triggered", session_id=session_id)

            if steps:
                last_observation = steps[-1]["output"]

                answer = (
                    "İsteğinizi tam olarak tamamlayamadım ancak "
                    "bulduklarım şunlar:\n\n"
                    f"{last_observation}"
                )

            else:
                answer = (
                    "Üzgünüm, isteğinizi işlerken yeterli adım "
                    "tamamlanamadı. Lütfen sorunuzu daha açık belirtin."
                )

        response = AgentResponse(
            answer=answer,
            steps=steps,
            tools_used=list(set(tools_used)),
            intent=detect_intent(user_input),
        )

        # ---------------------------------------------------
        # CACHE WRITE
        # ---------------------------------------------------
        try:
            await cache.set(
                prefix=cache_prefix,
                query=normalized_input,
                value=asdict(response),
                ttl=3600,
            )

            logger.info("agent_cache_written", session_id=session_id, cache_prefix=cache_prefix, ttl=3600)

        except Exception as e:
            logger.warning("agent_cache_write_failed", session_id=session_id, cache_prefix=cache_prefix, error=str(e))

        return response

    def clear_session(self, session_id: str) -> bool:
        """Belirli bir session'ı ve geçmişini siler."""
        if session_id in self._sessions:
            del self._sessions[session_id]
            logger.info("session_deleted", session_id=session_id)
            return True
        return False

    def session_exists(self, session_id: str) -> bool:
        return session_id in self._sessions

    @property
    def active_session_count(self) -> int:
        return len(self._sessions)


# Singleton — uygulama boyunca tek bir CommerceAgent örneği
commerce_agent = CommerceAgent()