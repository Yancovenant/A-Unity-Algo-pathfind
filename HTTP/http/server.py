from werkzeug.wrappers import Request, Response
from werkzeug.routing import Map, Rule
from werkzeug.serving import run_simple
from .layout import render_layout
import 


def load_static_file(filename):
    path = os.path.join(os.path.dirname(__file__), 'static', filename)
    if not os.path.exists(path):
        return Response('File not found', status=404)
    with open(path, 'rb') as f:
        content = f.read()
    return Response(content, mimetype='text/html')

def create_app():
    url_map = Map([
        Rule('/', endpoint='index'),
        Rule('/static/<path:filename>', endpoint='static'),
        Rule('/api/state', endpoint='state'),
    ])

    def app(environ, start_response):
        request = Request(environ)
        adapter = url_map.bind_to_environ(environ)
        try:
            endpoint, values = adapter.match()
            if endpoint == 'index':
                body = render_layout('index.xml')
                return Response(body, mimetype='text/html')(environ, start_response)
            elif endpoint == 'static':
                return load_static_file(values['filename'])(environ, start_response)
            elif endpoint == 'state':
                from .routes.state import handle_state
                return handle_state()(environ, start_response)
        except Exception as e:
            return Response(f"Error: {e}", status=500)(environ, start_response)

    return app

def run_server():
    app = create_app()
    print("Serving on http://localhost:8080")
    run_simple('localhost', 8080, app, threaded=True)