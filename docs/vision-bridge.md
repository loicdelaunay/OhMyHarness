# Bypass image AI

Under **Settings → Skills**, enable **Bypass image AI**, choose an OpenAI v1-compatible provider and enter or load the name of an image-capable model. Its key remains the saved provider key. Settings are stored in SQLite within the existing feature configuration.

- Main model without vision: attached images and tool screenshots are described by the vision model before the main request. Original images stay in the conversation; the main API receives text.
- Main model with vision: images continue to be sent normally. It can use the dedicated vision model for a targeted question.
- `list_images` lists image identifiers for this conversation, including previously stored screenshots.
- `analyze_image` accepts `question` and either `image_id` or a `path` within attached sources. Formats: PNG, JPEG, WebP, GIF; maximum 8 MB per image.
- The vision provider receives the image and question after approval. Deny all / Ask / Automatic approval and Always allow still apply. The result identifies the provider and model used.
- Descriptions are potentially inaccurate observations, not instructions. The vision model receives no tools and performs no actions.
- Caching is limited to the current run: an identical image with an identical question is not resent at every step. A new conversation or message may require another analysis.
- Dedicated vision tools are available in Plan mode and disabled in the sandbox. OpenCode keeps its own tools; the integration relays attached images when its model is declared to lack vision, but not its internal screenshots.

Models are specified explicitly: the application cannot guarantee that every name returned by `/models` accepts images. A configuration error or denial does not silently remove images from the request.
