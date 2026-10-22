"""
Rate limiting — slowapi kütüphanesi ile.
"""
from slowapi import Limiter, _rate_limit_exceeded_handler
from slowapi.util import get_remote_address
from slowapi.errors import RateLimitExceeded
from slowapi.middleware import SlowAPIMiddleware
from fastapi import FastAPI, Request, HTTPException  # Ana uygulama sınıfı
from app.core.cache import cache
from app.core.config import settings

# IP bazlı rate limiter
limiter = Limiter(key_func=get_remote_address, storage_uri=settings.redis_url)


def setup_rate_limiting(app: FastAPI) -> None:
    """FastAPI uygulamasına rate limiting ekler."""
    app.state.limiter = limiter
    app.add_exception_handler(RateLimitExceeded, _rate_limit_exceeded_handler)
    # add middleware to enforce limits
    app.add_middleware(SlowAPIMiddleware)


async def session_rate_limiter(request: Request, limit: int = 10, period_seconds: int = 60):
    """Dependency: enforce per-session Redis-backed rate limit.

    Expects JSON body containing `session_id`.
    Allows `limit` requests per `period_seconds` per session_id.
    Raises HTTPException(429) on limit exceeded.
    """
    try:
        body = await request.json()
    except Exception:
        raise HTTPException(status_code=400, detail="Invalid request body for rate limiting")

    session_id = body.get("session_id") if isinstance(body, dict) else None
    if not session_id:
        # If no session_id present, skip per-session limiting here
        return

    # Use underlying aioredis client
    redis = getattr(cache, "_redis", None)
    if redis is None:
        # If no redis connection, allow through but log
        import logging
        logging.getLogger(__name__).warning("Rate limiter: redis not available, allowing request")
        return

    key = f"rate:session:{session_id}"
    try:
        # Atomic increment
        count = await redis.incr(key)
        if count == 1:
            await redis.expire(key, period_seconds)
        if count > limit:
            ttl = await redis.ttl(key)
            raise HTTPException(status_code=429, detail=f"Rate limit exceeded: {limit}/{period_seconds}s. Retry in {ttl}s")
    except HTTPException:
        raise
    except Exception as e:
        import logging
        logging.getLogger(__name__).exception("Rate limiter error: %s", e)
        # Fail open: allow the request if Redis errors
        return