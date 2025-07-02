from ..qweb import qweb_render
from ..agent_stream import get_active_agents
from .base import Website, route
import os

class WebsiteMonitor(Website):
    @route('/monitor', methods=['GET'], group='website')
    async def monitor(self, request):
        agents = get_active_agents()
        html = qweb_render(
            os.path.join(os.path.dirname(__file__), '../templates/monitor.xml'),
            context={'agents': agents}
        )
        return html 