FROM python:3.11-slim

WORKDIR /app

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

COPY requirements.txt .
RUN cp requirements.txt requirements.docker.txt \
	&& sed -i 's/^torchaudio==.*/torchaudio==2.2.2/' requirements.docker.txt \
	&& sed -i 's/^torchvision==.*/torchvision==0.17.2/' requirements.docker.txt \
	&& pip install --no-cache-dir -r requirements.docker.txt

COPY . .

EXPOSE 8000

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
