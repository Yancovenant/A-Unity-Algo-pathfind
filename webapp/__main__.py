# HTTP/webapp/__main__.py
# Entrypoint for running the ASGI app with uvicorn
import uvicorn
from .asgi_app import app

def main():
    uvicorn.run("webapp.asgi_app:app", host="0.0.0.0", port=8000)

if __name__ == "__main__":
    main()