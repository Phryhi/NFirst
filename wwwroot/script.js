async function enviarPasta() {
    const input = document.getElementById("pasta");
    const resultado = document.getElementById("resultado");

    if (input.files.length === 0) {
        resultado.innerText = "Selecione uma pasta.";
        return null;
    }
    const formData = new FormData();
    for (const arquivo of input.files) {
        formData.append("arquivos", arquivo, arquivo.name);
    }

    try {
        const resposta = await fetch("/Notas/EnviarArquivos", {
            method: "POST",
            body: formData
        });

        let dados = null;
        try {
            dados = await resposta.json();
        } catch {
            
        }

        if (!resposta.ok) {
            const mensagemErro = dados?.mensagem || `Erro ${resposta.status}: O servidor não conseguiu processar a requisição.`;
            resultado.innerText = mensagemErro;
            return null;
        }

        resultado.innerText = dados?.mensagem || "Arquivos enviados com sucesso.";
        return dados?.notas || null;
    } catch (erro) {
        resultado.innerText = "Erro de conexão com o servidor.";
        return null;
    }
}

const $ = (id) => document.getElementById(id);
const dormir = (ms) => new Promise((r) => setTimeout(r, ms));

$("pasta").addEventListener("change", () => {
    const n = $("pasta").files.length;
    $("rotuloPasta").textContent = n ? `${n} arquivo(s) selecionado(s)` : "Selecionar pasta";
});

let jsonGerado = null;

async function processar() {
    if ($("pasta").files.length === 0) {
        await enviarPasta();
        return;
    }

    $("btnEnviar").disabled = true;
    $("btnBaixar").disabled = true;
    $("containerJson").hidden = true;

    jsonGerado = null;
    const pararScanner = iniciarScanner();

    try {
        const [notasServidor] = await Promise.all([enviarPasta(), dormir(2500)]);

        if (Array.isArray(notasServidor) && notasServidor.length > 0) {
            jsonGerado = notasServidor;
            $("btnBaixar").disabled = false;
            $("jsonViewer").textContent = JSON.stringify(jsonGerado, null, 2);
            $("containerJson").hidden = false;
        }
    } finally {
        pararScanner();
        $("btnEnviar").disabled = false;
    }
}

function baixarJson() {
    if (!jsonGerado) return;
    const blob = new Blob([JSON.stringify(jsonGerado, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `nfirst-${new Date().toISOString().slice(0, 10)}.json`;
    a.click();
    URL.revokeObjectURL(url);
}

function copiarJson() {
    if (!jsonGerado) return;
    navigator.clipboard.writeText(JSON.stringify(jsonGerado, null, 2));
    const btn = document.querySelector(".btn-copiar");
    btn.textContent = "Copiado!";
    setTimeout(() => { btn.textContent = "Copiar JSON"; }, 2000);
}

function iniciarScanner() {
    const scanner = $("scanner");
    const lupa = $("lupa");
    const linhas = [...scanner.querySelectorAll(".linha")];
    let ativo = true;

    scanner.hidden = false;

    (async () => {
        while (ativo) {
            linhas.forEach((l) => (l.style.fill = ""));
            lupa.style.transform = "translate(120px, 20px)";
            await dormir(600);
            for (const linha of linhas) {
                if (!ativo) return;
                const y = Number(linha.getAttribute("y")) + 4;
                lupa.style.transform = `translate(${100 + Math.random() * 30}px, ${y}px)`;
                await dormir(550);
                linha.style.fill = linha.dataset.cor;
                await dormir(150);
            }
            await dormir(700);
        }
    })();

    return () => { ativo = false; scanner.hidden = true; };
}