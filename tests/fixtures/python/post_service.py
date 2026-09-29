from .helpers import to_slug

class PostService:
    def process(self, title: str):
        return to_slug(title)
