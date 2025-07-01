from werkzeug.wrappers import Request, Response
from werkzeug.routing import Map, Rule
import inspect

from werkzeug.serving import run_simple
from .layout import render_layout

from webapp.router import register_all_routes
from webapp.controller.route import route as controller_route
 

class Application:
    def __init__(self):
        self.url_map = Map()
        self.handlers = {}
    
    def __call__(self, environ, start_response):
        response = self.dispatch(environ)
        return response(environ, start_response)

    def add_route(self, path, endpoint, methods=['GET']):
        rule = Rule(path, endpoint=endpoint.__name__, methods=methods)
        self.url_map.add(rule)
        self.handlers[endpoint.__name__] = endpoint
    
    def dispatch(self, environ):
        request = Request(environ)
        adapter = self.url_map.bind_to_environ(environ)
        try:
            endpoint, values = adapter.match()
            handler = self.handlers[endpoint]
            if 'request' in inspect.signature(handler).parameters:
                response = handler(request=request, **values)
            else:
                response = handler(**values)
            if isinstance(response, str):
                return Response(response, content_type='text/html')
            return response
        except Exception as e:
            return Response(f"404 Not Found: {e}", status=404)



def load_static_file(filename):
    path = os.path.join(os.path.dirname(os.path.dirname(__file__)), 'static', filename)
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
    app = Application()
    register_all_routes(app)
    print("Serving on http://localhost:8080")
    run_simple('localhost', 8080, app, threaded=True)
