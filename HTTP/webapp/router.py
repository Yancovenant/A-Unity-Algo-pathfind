from werkzeug.routing import Rule
registered_routes = []

def route(path, methods=['GET']):
    def decorator(func):
        registered_routes.append((path, func, methods))
        return func
    return decorator

def register_all_routes(app):
    for path, func, methods in registered_routes:
        app.add_route(path, func, methods)