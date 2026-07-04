using System.Drawing;
using System.Text;
using SharpGL;
using SharpGL.Shaders;
using SharpGL.VertexBuffers;
using VE3NEA;

namespace SkyRoof
{
  public class PolylineRenderer
  {
    private OpenGL gl;
    private ShaderProgram ShaderProgram;
    private VertexBufferArray VertexBufferArray;
    private VertexBuffer VertexBuffer;

    public PolylineRenderer(OpenGL gl)
    {
      this.gl = gl;

      gl.Enable(OpenGL.GL_BLEND);
      gl.BlendFunc(OpenGL.GL_SRC_ALPHA, OpenGL.GL_ONE_MINUS_SRC_ALPHA);

      CreateVba();
      LoadShaders();
    }

    private void LoadShaders()
    {
      ShaderProgram = new ShaderProgram();

      ShaderProgram.Create(gl,
        Encoding.ASCII.GetString(Properties.Resources.PolylineVertexShader),
        Encoding.ASCII.GetString(Properties.Resources.PolylineFragmentShader),
        null);

      ShaderProgram.AssertValid(gl);
    }

    private void CreateVba()
    {
      VertexBufferArray = new VertexBufferArray();
      VertexBufferArray.Create(gl);
      VertexBufferArray.Bind(gl);

      // x, y, set per draw call
      VertexBuffer = new VertexBuffer();
      VertexBuffer.Create(gl);
      VertexBuffer.Bind(gl);

      VertexBufferArray.Unbind(gl);
    }

    // points are in normalized device coordinates, as produced by EarthViewPanel.ComputeLocation.
    // OpenGL core profile clamps glLineWidth to 1, so we build a triangle strip to get a
    // line of the requested pixel width.
    private int[] viewport = new int[4]; // 0, 0, W, H
    internal void DrawPolyline(PointF[] points, Color color, float width = 3f)
    {
      if (points.Length < 2) return;

      gl.GetInteger(OpenGL.GL_VIEWPORT, viewport);
      // ndc <-> pixel scale factors (ndc spans [-1, 1] over the viewport)
      float sx = viewport[2] / 2f;
      float sy = viewport[3] / 2f;
      float half = width / 2f;

      // two strip vertices per point, offset perpendicular to the line in pixel space
      var data = new float[points.Length * 4];
      int idx = 0;

      for (int i = 0; i < points.Length; i++)
      {
        var prev = points[Math.Max(0, i - 1)];
        var next = points[Math.Min(points.Length - 1, i + 1)];

        // tangent in pixel space
        float tx = (next.X - prev.X) * sx;
        float ty = (next.Y - prev.Y) * sy;
        float len = MathF.Sqrt(tx * tx + ty * ty);
        if (len < 1e-6f) { tx = 1; ty = 0; len = 1; }
        tx /= len; ty /= len;

        // normal in pixel space, scaled to half width, then back to ndc
        float nx = -ty * half / sx;
        float ny = tx * half / sy;

        data[idx++] = points[i].X + nx;
        data[idx++] = points[i].Y + ny;
        data[idx++] = points[i].X - nx;
        data[idx++] = points[i].Y - ny;
      }

      ShaderProgram.Bind(gl);
      CheckError();

      VertexBufferArray.Bind(gl);
      CheckError();

      VertexBuffer.Bind(gl);
      VertexBuffer.SetData(gl, 0, data, false, 2);
      CheckError();

      gl.Uniform4(ShaderProgram.GetUniformLocation(gl, "in_color"),
        color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
      CheckError();

      gl.DrawArrays(OpenGL.GL_TRIANGLE_STRIP, 0, points.Length * 2);
      CheckError();

      VertexBufferArray.Unbind(gl);
      CheckError();

      ShaderProgram.Unbind(gl);
      CheckError();
    }

    internal void CheckError(bool log = true)
    {
      ExceptionLogger.CheckOpenglError(gl, log);
    }
  }
}
