# Use official Python image
FROM python:3.10-slim

# Set workdir
WORKDIR /app

# Install OS dependencies (libGL is required for OpenCV)
RUN apt-get update && apt-get install -y \
    libglib2.0-0 \
    libsm6 \
    libxext6 \
    libxrender1 \
    libgl1 \
    && rm -rf /var/lib/apt/lists/*

RUN pip install opencv-python

# Copy only requirements first
COPY requirements.txt .

# Install dependencies
RUN pip install --no-cache-dir -r requirements.txt

# Copy all files
COPY . .

# Expose port for Cloud Run
EXPOSE 8080

# Set environment variable for host/port
ENV PORT=8080

# Run your app
#CMD ["python", "-m", "webapp"]
CMD ["uvicorn", "webapp.asgi_app:app", "--host", "0.0.0.0", "--port", "8080"]
