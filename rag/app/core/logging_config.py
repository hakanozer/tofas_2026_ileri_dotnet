"""
Structured logging konfigürasyonu — structlog ile.
JSON formatlı loglar ELK/Datadog ile uyumludur.
"""
from __future__ import annotations

import logging
from logging.handlers import SysLogHandler
import socket
import sys

import structlog


def setup_logging(log_level: str = "INFO", logstash_host: str = "", logstash_port: int = 5514) -> None:
    """Uygulama geneli logging ayarlarını yapar."""
    level = getattr(logging, log_level.upper(), logging.INFO)

    shared_processors = [
        structlog.contextvars.merge_contextvars,
        structlog.processors.add_log_level,
        structlog.processors.TimeStamper(fmt="iso", utc=True),
    ]

    formatter = structlog.stdlib.ProcessorFormatter(
        processor=structlog.processors.JSONRenderer(),
        foreign_pre_chain=shared_processors,
    )

    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(formatter)

    root_logger = logging.getLogger()
    root_logger.handlers.clear()
    root_logger.addHandler(handler)

    # İstenirse local uygulama loglarını TCP syslog ile Logstash'e de gönder.
    if logstash_host:
        syslog_handler = SysLogHandler(
            address=(logstash_host, logstash_port),
            socktype=socket.SOCK_STREAM,
        )
        syslog_handler.setFormatter(formatter)
        root_logger.addHandler(syslog_handler)

    root_logger.setLevel(level)

    structlog.configure(
        processors=[
            *shared_processors,
            structlog.processors.StackInfoRenderer(),
            structlog.processors.format_exc_info,
            structlog.stdlib.ProcessorFormatter.wrap_for_formatter,
        ],
        wrapper_class=structlog.make_filtering_bound_logger(level),
        logger_factory=structlog.stdlib.LoggerFactory(),
        cache_logger_on_first_use=True,
    )
