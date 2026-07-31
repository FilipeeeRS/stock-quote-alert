/*
 * Cena 3D de fundo — um terreno infinito por onde a câmera avança para sempre.
 *
 * Tudo é calculado por pixel dentro do shader (raymarching): não há malha de
 * triângulos nem biblioteca de 3D. O JavaScript só desenha dois triângulos que
 * cobrem a tela e deixa a placa de vídeo fazer o resto.
 *
 * Se o navegador não tiver WebGL, ou o shader não compilar, o canvas é escondido
 * e sobra o fundo escuro do CSS — a página continua funcionando igual.
 */
(function cena3D() {
  const canvas = document.getElementById('cena');
  if (!canvas) return;

  const gl = canvas.getContext('webgl', { antialias: false, alpha: false, powerPreference: 'low-power' })
          || canvas.getContext('experimental-webgl');

  if (!gl) { canvas.style.display = 'none'; return; }

  const VERTEX = `
    attribute vec2 aPos;
    void main() { gl_Position = vec4(aPos, 0.0, 1.0); }
  `;

  const FRAGMENT = `
    precision highp float;
    uniform vec2  uRes;
    uniform float uTime;

    float hash(vec2 p) {
      return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
    }

    float ruido(vec2 p) {
      vec2 i = floor(p), f = fract(p);
      vec2 u = f * f * (3.0 - 2.0 * f);
      return mix(mix(hash(i),                  hash(i + vec2(1.0, 0.0)), u.x),
                 mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
    }

    // Altura do terreno num ponto do plano.
    float terreno(vec2 p) {
      float t = uTime * 0.22;
      float h  = sin(p.x * 0.32 + t * 1.1) * 0.42;
      h += sin(p.y * 0.26 - t * 0.85) * 0.38;
      h += (ruido(p * 0.3 + vec2(0.0, t * 0.5)) - 0.5) * 1.15;
      return h;
    }

    void main() {
      vec2 uv = (gl_FragCoord.xy - 0.5 * uRes) / uRes.y;

      // Câmera avançando para sempre, quase nivelada. O ponto de fuga fica
      // logo acima do centro: o terreno ocupa a metade de baixo e sobra céu
      // escuro atrás do conteúdo, que precisa continuar legível.
      vec3 ro = vec3(0.0, 3.1, uTime * 1.65);
      vec3 rd = normalize(vec3(uv.x, uv.y - 0.045, 1.0));

      float t = 0.5;
      float bateu = 0.0;
      vec3  p = ro;

      for (int i = 0; i < 92; i++) {
        p = ro + rd * t;
        float d = p.y - terreno(p.xz);
        if (d < 0.02 * t) { bateu = 1.0; break; }
        t += clamp(d * 0.55, 0.09, 2.4);
        if (t > 68.0) break;
      }

      vec3 cor = vec3(0.021, 0.026, 0.041);

      if (bateu > 0.5) {
        // A linha engrossa com a distância: sem isso o grid vira ruído
        // tremido perto do horizonte (aliasing).
        float w = 0.014 + t * 0.0075;
        vec2 g = abs(fract(p.xz) - 0.5);
        float linha = max(smoothstep(w, 0.0, g.x), smoothstep(w, 0.0, g.y));

        float nevoa = exp(-t * 0.075);
        float alto  = clamp(p.y * 0.42 + 0.5, 0.0, 1.0);

        vec3 corLinha = mix(vec3(0.10, 0.30, 0.88), vec3(0.36, 0.80, 1.0), alto);

        cor += corLinha * linha * nevoa * 1.15;
        cor += vec3(0.030, 0.065, 0.17) * nevoa * 0.55;
      }

      // Brilho no horizonte, para a cena não terminar num corte seco.
      float horizonte = exp(-abs(uv.y - 0.045) * 9.0);
      cor += vec3(0.05, 0.13, 0.36) * horizonte * 0.42;

      // Estrelas: célula fina + distância ao centro dela. Sem a distância,
      // saem quadrados em vez de pontos.
      vec2 sc = (uv + vec2(3.0, 1.5)) * 95.0;
      float e = hash(floor(sc));
      if (e > 0.987 && uv.y > 0.075) {
        float d = length(fract(sc) - 0.5);
        float cintila = 0.45 + 0.55 * sin(uTime * 1.6 + e * 80.0);
        cor += vec3(0.6, 0.74, 1.0) * smoothstep(0.36, 0.0, d) * cintila * 0.42;
      }

      gl_FragColor = vec4(cor, 1.0);
    }
  `;

  function compilar(tipo, fonte) {
    const s = gl.createShader(tipo);
    gl.shaderSource(s, fonte);
    gl.compileShader(s);
    if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) {
      console.warn('Shader não compilou:', gl.getShaderInfoLog(s));
      return null;
    }
    return s;
  }

  const vs = compilar(gl.VERTEX_SHADER, VERTEX);
  const fs = compilar(gl.FRAGMENT_SHADER, FRAGMENT);
  if (!vs || !fs) { canvas.style.display = 'none'; return; }

  const prog = gl.createProgram();
  gl.attachShader(prog, vs);
  gl.attachShader(prog, fs);
  gl.linkProgram(prog);
  if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
    console.warn('Programa não linkou:', gl.getProgramInfoLog(prog));
    canvas.style.display = 'none';
    return;
  }
  gl.useProgram(prog);

  // Um triângulo grande o bastante para cobrir a tela inteira.
  const buf = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, buf);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
  const aPos = gl.getAttribLocation(prog, 'aPos');
  gl.enableVertexAttribArray(aPos);
  gl.vertexAttribPointer(aPos, 2, gl.FLOAT, false, 0, 0);

  const uRes = gl.getUniformLocation(prog, 'uRes');
  const uTime = gl.getUniformLocation(prog, 'uTime');

  function redimensionar() {
    // Segura a resolução: calcular por pixel fica caro em tela 4K, e o fundo
    // está desfocado por trás do véu de qualquer jeito.
    const dpr = Math.min(window.devicePixelRatio || 1, 1.5);
    const l = Math.max(1, Math.floor(window.innerWidth * dpr));
    const a = Math.max(1, Math.floor(window.innerHeight * dpr));
    if (canvas.width !== l || canvas.height !== a) {
      canvas.width = l;
      canvas.height = a;
      gl.viewport(0, 0, l, a);
    }
  }

  const semMovimento = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  let inicio = performance.now();
  let rodando = true;

  function quadro(agora) {
    if (!rodando) return;
    redimensionar();
    const t = semMovimento ? 8.0 : (agora - inicio) / 1000;
    gl.uniform2f(uRes, canvas.width, canvas.height);
    gl.uniform1f(uTime, t);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    if (!semMovimento) requestAnimationFrame(quadro);
  }

  requestAnimationFrame(quadro);

  // Não gastar bateria e GPU com a aba escondida.
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
      rodando = false;
    } else if (!rodando) {
      rodando = true;
      inicio = performance.now() - 8000;
      requestAnimationFrame(quadro);
    }
  });

  canvas.addEventListener('webglcontextlost', (e) => {
    e.preventDefault();
    rodando = false;
    canvas.style.display = 'none';
  });
})();
