from werkzeug.wrappers import Response
import json

def handle_state():
    data = {
        "agents": {
            "AUGV_1": {"pos": [2, 4], "status": "moving"},
            "AUGV_2": {"pos": [5, 8], "status": "waiting"}
        }
    }
    return lambda environ, start_response: Response(
        json.dumps(data), mimetype='application/json')(environ, start_response)
