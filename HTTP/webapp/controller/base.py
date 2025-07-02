# HTTP/webapp/controller/base.py
# Base controller classes and decorators for ASGI app
import inspect
from functools import wraps

# --- Decorator registry ---
class RouteRegistry:
    def __init__(self):
        self.http_routes = []  # (path, methods, handler, group, type)
        self.ws_routes = []    # (path, handler, group)
        self.groups = {}

    def add_http(self, path, methods, handler, group, type_):
        self.http_routes.append((path, methods, handler, group, type_))
        if group:
            self.groups.setdefault(group, []).append(handler)

    def add_ws(self, path, handler, group):
        self.ws_routes.append((path, handler, group))
        if group:
            self.groups.setdefault(group, []).append(handler)

route_registry = RouteRegistry()

def route(path, *, methods=["GET"], group=None, type=None):
    def decorator(func):
        route_registry.add_http(path, methods, func, group, type)
        return func
    return decorator

def wsroute(path, *, group=None):
    def decorator(func):
        route_registry.add_ws(path, func, group)
        return func
    return decorator

# --- Class-based route grouping ---
class ControllerMeta(type):
    def __new__(mcs, name, bases, attrs):
        cls = super().__new__(mcs, name, bases, attrs)
        for attr_name, attr in attrs.items():
            if hasattr(attr, "_is_route") or hasattr(attr, "_is_wsroute"):
                setattr(cls, attr_name, attr)
        return cls

class Controller(metaclass=ControllerMeta):
    pass

# --- Base Website controller (for extension) ---
class Website(Controller):
    pass 