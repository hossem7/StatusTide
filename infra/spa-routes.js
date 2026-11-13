// CloudFront Function (viewer request) for the web bucket only.
// A path with no file extension is an app route, such as /incidents/{id} or
// /confirm-email, so it gets index.html and React picks the screen.
// Files such as /assets/index-abc123.js pass through unchanged.
function handler(event) {
  var request = event.request;
  var lastSegment = request.uri.substring(request.uri.lastIndexOf('/') + 1);

  if (lastSegment.indexOf('.') === -1) {
    request.uri = '/index.html';
  }

  return request;
}
