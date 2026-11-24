/**
 * Front-end do NFirst: envia uma pasta de XMLs para a API, mostra o resultado
 * e baixa os arquivos gerados pelo servidor.
 */

const URL_ENVIAR_ARQUIVOS = "/Notas/EnviarArquivos";
const URL_DOWNLOAD = "/Notas/Download";

const ROTULO_SELECIONAR_PASTA = "Selecionar pasta";
const ROTULO_COPIAR_JSON = "Copiar JSON";

// A animação dura no mínimo este tempo, mesmo que o servidor responda antes,
// para ela não aparecer e sumir num piscar de olhos.
const DURACAO_MINIMA_SCANNER_MS = 2500;
// Disparar vários downloads de uma vez pode ser bloqueado pelo navegador.
const INTERVALO_ENTRE_DOWNLOADS_MS = 300;
const DURACAO_AVISO_COPIADO_MS = 2000;

// Animação da lupa. As posições são coordenadas do SVG do index.html.
const LUPA_POSICAO_INICIAL = "translate(120px, 20px)";
const LUPA_X_MINIMO = 100;
const LUPA_X_VARIACAO = 30;
// Metade da altura de uma linha do documento (8), para a lente ficar centralizada nela.
const LUPA_AJUSTE_Y = 4;
const CHANCE_LINHA_VERDE = 0.5;
const PAUSA_APOS_REINICIAR_MS = 600;
// Um pouco maior que a transição de 0.5s da #lupa no style.css:
// a lupa chega na linha antes de ela ser marcada.
const PAUSA_MOVIMENTO_LUPA_MS = 550;
const PAUSA_APOS_MARCAR_MS = 150;
const PAUSA_ENTRE_CICLOS_MS = 700;

let notasProcessadas = null;
let arquivosGerados = [];

const $ = (id) => document.getElementById(id);
const dormir = (ms) => new Promise((resolver) => setTimeout(resolver, ms));

/**
 * Envia os arquivos da pasta selecionada para a API e mostra a mensagem de resultado.
 *
 * Efeito colateral: atualiza `arquivosGerados` com os nomes devolvidos pelo servidor.
 *
 * @returns {Promise<Array|null>} Resumo das notas salvas, ou null se nenhuma pasta
 *   foi selecionada ou se houve erro.
 */
async function enviarPasta() {
    const resultado = $("resultado");
    const arquivos = $("pasta").files;

    if (arquivos.length === 0) {
        resultado.innerText = "Selecione uma pasta.";
        return null;
    }

    try {
        const resposta = await fetch(URL_ENVIAR_ARQUIVOS, {
            method: "POST",
            body: montarFormData(arquivos)
        });
        const dados = await lerJsonOuNulo(resposta);

        if (!resposta.ok) {
            resultado.innerText = dados?.mensagem || `Erro ${resposta.status}: O servidor não conseguiu processar a requisição.`;
            return null;
        }

        resultado.innerText = dados?.mensagem || "Arquivos enviados com sucesso.";
        arquivosGerados = dados?.arquivos || [];
        return dados?.notas || null;
    } catch (erro) {
        resultado.innerText = "Erro de conexão com o servidor.";
        return null;
    }
}

// O nome "arquivos" precisa ser igual ao parâmetro do NotasController.
function montarFormData(arquivos) {
    const formData = new FormData();

    for (const arquivo of arquivos) {
        formData.append("arquivos", arquivo, arquivo.name);
    }

    return formData;
}

// O servidor pode responder sem JSON (ex.: erro 500 com página HTML).
// Nesse caso devolvemos null; qualquer outro erro continua subindo.
async function lerJsonOuNulo(resposta) {
    try {
        return await resposta.json();
    } catch (erro) {
        if (erro instanceof SyntaxError) {
            return null;
        }

        throw erro;
    }
}

$("pasta").addEventListener("change", () => {
    const quantidade = $("pasta").files.length;
    $("rotuloPasta").textContent = quantidade ? `${quantidade} arquivo(s) selecionado(s)` : ROTULO_SELECIONAR_PASTA;
});

/**
 * Fluxo do botão "Enviar pasta": envia os arquivos, mostra a animação e,
 * se o servidor devolver notas, exibe o JSON e habilita o download.
 */
async function processarPasta() {
    // Sem pasta, enviarPasta só mostra o aviso "Selecione uma pasta."
    if ($("pasta").files.length === 0) {
        await enviarPasta();
        return;
    }

    $("btnEnviar").disabled = true;
    $("btnBaixar").disabled = true;
    $("containerJson").hidden = true;

    notasProcessadas = null;
    arquivosGerados = [];
    const pararScanner = iniciarScanner();

    try {
        const [notasServidor] = await Promise.all([enviarPasta(), dormir(DURACAO_MINIMA_SCANNER_MS)]);

        if (Array.isArray(notasServidor) && notasServidor.length > 0) {
            notasProcessadas = notasServidor;
            $("btnBaixar").disabled = false;
            $("jsonViewer").textContent = JSON.stringify(notasProcessadas, null, 2);
            $("containerJson").hidden = false;
        }
    } finally {
        pararScanner();
        $("btnEnviar").disabled = false;
    }
}

/**
 * Baixa, um por vez, os arquivos TXT gerados no último envio.
 */
async function baixarArquivosGerados() {
    if (arquivosGerados.length === 0) {
        alert("Nenhum arquivo foi gerado neste envio.");
        return;
    }

    for (const nome of arquivosGerados) {
        const link = document.createElement("a");
        link.href = `${URL_DOWNLOAD}/${encodeURIComponent(nome)}`;
        link.download = nome;
        link.click();
        await dormir(INTERVALO_ENTRE_DOWNLOADS_MS);
    }
}

function copiarJson() {
    if (!notasProcessadas) {
        return;
    }

    navigator.clipboard.writeText(JSON.stringify(notasProcessadas, null, 2));

    const botao = document.querySelector(".btn-copiar");
    botao.textContent = "Copiado!";
    setTimeout(() => { botao.textContent = ROTULO_COPIAR_JSON; }, DURACAO_AVISO_COPIADO_MS);
}

/**
 * Mostra a animação da lupa varrendo o documento.
 *
 * @returns {Function} Função que para a animação e esconde o scanner.
 */
function iniciarScanner() {
    const scanner = $("scanner");
    const lupa = $("lupa");
    const linhas = [...scanner.querySelectorAll(".linha")];
    let ativo = true;

    scanner.hidden = false;

    // Não usamos await: a animação roda em segundo plano até `ativo` virar false.
    animarScanner(linhas, lupa, () => ativo);

    return () => { ativo = false; scanner.hidden = true; };
}

// Repete o ciclo: limpa as linhas, volta a lupa ao início e varre linha por linha.
async function animarScanner(linhas, lupa, estaAtivo) {
    while (estaAtivo()) {
        linhas.forEach((linha) => linha.classList.remove("verde"));
        lupa.style.transform = LUPA_POSICAO_INICIAL;
        await dormir(PAUSA_APOS_REINICIAR_MS);

        for (const linha of linhas) {
            if (!estaAtivo()) {
                return;
            }

            await varrerLinha(linha, lupa);
        }

        await dormir(PAUSA_ENTRE_CICLOS_MS);
    }
}

async function varrerLinha(linha, lupa) {
    const y = Number(linha.getAttribute("y")) + LUPA_AJUSTE_Y;
    const x = LUPA_X_MINIMO + Math.random() * LUPA_X_VARIACAO;

    lupa.style.transform = `translate(${x}px, ${y}px)`;
    await dormir(PAUSA_MOVIMENTO_LUPA_MS);

    // Sorteio: true deixa a linha verde, false mantém a cor original.
    linha.classList.toggle("verde", Math.random() < CHANCE_LINHA_VERDE);
    await dormir(PAUSA_APOS_MARCAR_MS);
}