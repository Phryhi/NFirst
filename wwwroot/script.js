async function enviarPasta() {

    const input = document.getElementById("pasta");
    const resultado = document.getElementById("resultado");

    if (input.files.length === 0) {
        resultado.innerText = "Selecione uma pasta.";
        return;
    }

    const formData = new FormData();

    for (const arquivo of input.files) {
        formData.append("arquivos", arquivo);
    }

    const resposta = await fetch("/Notas/EnviarArquivos", {
        method: "POST",
        body: formData
    });

    if (!resposta.ok) {
        resultado.innerText = `Erro ao enviar arquivos: ${resposta.status}`;
        return;
    }

    const dados = await resposta.json();
    resultado.innerText = dados.mensagem;
}