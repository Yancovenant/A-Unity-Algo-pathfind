#main.py
from webapp.server import run_server
from webapp.controller.route import start_ws_thread_once

if __name__ == "__main__":
    start_ws_thread_once()
    run_server()
