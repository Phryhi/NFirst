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

// ==========================================
// ELEMENTOS DO DOM
// ==========================================
const modalOverlay = document.getElementById('modalOverlay');
const btnOpenProcess = document.getElementById('btnOpenProcess');
const btnOpenProcessNav = document.getElementById('btnOpenProcessNav');
const btnCloseModal = document.getElementById('btnCloseModal');
const dropArea = document.getElementById('dropArea');
const fileInput = document.getElementById('fileInput');
const btnProcess = document.getElementById('btnProcess');

const staticIcon = document.getElementById('staticIcon');
const animatedIcon = document.getElementById('animatedIcon');
const dropText = document.getElementById('dropText');

const magnifierVisual = document.getElementById('magnifierVisual');
const maskCircle = document.getElementById('maskCircle');

// ==========================================
// ESTADO DA APLICAÇÃO
// ==========================================
let animationFrameId = null;
let startTime = null;
let selectedFiles = [];
const DURATION = 4000;

let totalProcessados = 1248;
let totalValidas = 1192;
let valorTotalCredito = 18430.55;

// ==========================================
// ANIMAÇÃO SVG
// ==========================================
function lerp(start, end, t) {
    return start * (1 - t) + end * t;
}

function animate(currentTime) {
    if (!startTime) startTime = currentTime;
    const elapsedTime = (currentTime - startTime) % DURATION;
    const progress = elapsedTime / DURATION;

    let translateX, translateY, rotateZ;

    if (progress < 0.20) {
        translateX = lerp(0, -5, progress / 0.20);
        translateY = lerp(0, 15, progress / 0.20);
        rotateZ = lerp(0, -5, progress / 0.20);
    } else if (progress < 0.80) {
        const scanProgress = (progress - 0.20) / 0.60;
        translateX = -5 + (Math.sin(scanProgress * Math.PI * 3) * 15);
        translateY = lerp(15, 65, scanProgress);
        rotateZ = -5 + (Math.sin(scanProgress * Math.PI * 3) * 8);
    } else {
        const returnProgress = (progress - 0.80) / 0.20;
        translateX = lerp(10, 0, returnProgress);
        translateY = lerp(65, 0, returnProgress);
        rotateZ = lerp(5, 0, returnProgress);

        if (returnProgress > 0.1 && returnProgress < 0.2) {
            animatedIcon.classList.add('analysis-complete');
            setTimeout(() => animatedIcon.classList.remove('analysis-complete'), 600);
        }
    }

    if (magnifierVisual && maskCircle) {
        magnifierVisual.style.transform = `translate(${translateX}px, ${translateY}px) rotate(${rotateZ}deg)`;
        maskCircle.setAttribute('cx', 35 + translateX);
        maskCircle.setAttribute('cy', 35 + translateY);
    }

    animationFrameId = requestAnimationFrame(animate);
}

function startAnimation() {
    startTime = null;
    staticIcon.style.display = 'none';
    animatedIcon.style.display = 'flex';
    animationFrameId = requestAnimationFrame(animate);
}

function stopAnimation() {
    if (animationFrameId) {
        cancelAnimationFrame(animationFrameId);
        animationFrameId = null;
    }
    animatedIcon.style.display = 'none';
    staticIcon.style.display = 'block';
}

// ==========================================
// GERENCIAMENTO DO MODAL
// ==========================================
function openModal() {
    modalOverlay.classList.add('active');
}

function closeModal() {
    modalOverlay.classList.remove('active');
    resetModalState();
}

function resetModalState() {
    stopAnimation();
    selectedFiles = [];
    fileInput.value = '';
    dropArea.classList.remove('error', 'success');
    dropText.innerHTML = 'Arraste e solte seus arquivos ou <span class="browse-btn">procure no computador</span>';
    btnProcess.disabled = false;
    btnProcess.className = 'btn-primary btn-block';
    btnProcess.innerHTML = '<i class="fa-solid fa-gear"></i> Processar Agora';
}

btnOpenProcess.addEventListener('click', openModal);
btnOpenProcessNav.addEventListener('click', (e) => {
    e.preventDefault();
    openModal();
});
btnCloseModal.addEventListener('click', closeModal);
modalOverlay.addEventListener('click', (e) => {
    if (e.target === modalOverlay) closeModal();
});

// ==========================================
// SELEÇÃO E DRAG & DROP DE ARQUIVOS
// ==========================================
dropArea.addEventListener('click', () => fileInput.click());

dropArea.addEventListener('dragover', (e) => {
    e.preventDefault();
    dropArea.classList.add('drag-over');
});

dropArea.addEventListener('dragleave', () => {
    dropArea.classList.remove('drag-over');
});

dropArea.addEventListener('drop', (e) => {
    e.preventDefault();
    dropArea.classList.remove('drag-over');
    if (e.dataTransfer.files.length > 0) {
        handleFileSelection(Array.from(e.dataTransfer.files));
    }
});

fileInput.addEventListener('change', () => {
    if (fileInput.files.length > 0) {
        handleFileSelection(Array.from(fileInput.files));
    }
});

function handleFileSelection(files) {
    dropArea.classList.remove('error');
    // Filtra apenas ficheiros com extensão .xml
    const xmlFiles = files.filter(file => file.name.toLowerCase().endsWith('.xml'));

    if (xmlFiles.length === 0) {
        showError('Nenhum arquivo XML válido foi selecionado.');
        selectedFiles = [];
        return;
    }

    selectedFiles = xmlFiles;
    dropText.innerHTML = `<strong>${selectedFiles.length}</strong> arquivo(s) XML selecionado(s).`;
}

function showError(msg) {
    dropArea.classList.add('error');
    dropText.innerHTML = `<span style="color: #dc2626; font-weight: 500;"><i class="fa-solid fa-triangle-exclamation"></i> ${msg}</span>`;
    btnProcess.disabled = false;
    btnProcess.innerHTML = '<i class="fa-solid fa-gear"></i> Tentar Novamente';
}

// ==========================================
// PARSER REAL DE XML NO BROWSER
// ==========================================
btnProcess.addEventListener('click', async () => {
    if (selectedFiles.length === 0) {
        showError('Por favor, selecione ao menos um arquivo XML antes de processar.');
        return;
    }

    startAnimation();
    dropText.textContent = 'Analisando arquivos XML... Aguarde.';
    btnProcess.disabled = true;
    btnProcess.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Processando...';

    const parsedResults = [];

    // Processa cada ficheiro XML no próprio navegador
    for (const file of selectedFiles) {
        try {
            const content = await readFileAsText(file);
            const parsedData = parseXmlContent(content, file.name);
            parsedResults.push(parsedData);
        } catch (err) {
            parsedResults.push({
                chave: 'ERRO_LEITURA_' + file.name,
                data: new Date().toLocaleDateString('pt-BR'),
                valor: 'R$ 0,00',
                credito: '—',
                status: 'Inválida',
                rawValor: 0,
                rawCredito: 0,
                isValid: false
            });
        }
    }

    
    setTimeout(() => {
        stopAnimation();
        dropText.textContent = 'Processamento concluído com sucesso!';
        btnProcess.innerHTML = '<i class="fa-solid fa-circle-check"></i> Sucesso!';
        btnProcess.style.backgroundColor = '#16a34a';

        // Atualiza a interface com os dados reais lidos
        renderNewData(parsedResults);

        setTimeout(() => {
            closeModal();
        }, 1200);
    }, 2000);
});

function readFileAsText(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = () => reject(reader.error);
        reader.readAsText(file);
    });
}

function parseXmlContent(xmlString, filename) {
    const parser = new DOMParser();
    const xmlDoc = parser.parseFromString(xmlString, "text/xml");

    // Verifica erro de parse de XML
    if (xmlDoc.getElementsByTagName("parsererror").length > 0) {
        throw new Error("XML inválido");
    }

    // Procura por nós comuns de NFe / NFCe
    const infNfe = xmlDoc.getElementsByTagName("infNfe")[0] || xmlDoc.getElementsByTagName("infNFe")[0];
    const chNFe = xmlDoc.getElementsByTagName("chNFe")[0];
    const dhEmi = xmlDoc.getElementsByTagName("dhEmi")[0] || xmlDoc.getElementsByTagName("dEmi")[0];
    const vNF = xmlDoc.getElementsByTagName("vNF")[0];
    const mod = xmlDoc.getElementsByTagName("mod")[0];

    // Extrai a chave de acesso (ID do infNfe ou tag chNFe)
    let chave = '';
    if (chNFe) {
        chave = chNFe.textContent;
    } else if (infNfe && infNfe.getAttribute("Id")) {
        chave = infNfe.getAttribute("Id").replace('NFe', '');
    } else {
        chave = '35' + Math.floor(Math.random() * 1e42).toString().padStart(42, '0');
    }

    // Extrai Valor
    const valorNum = vNF ? parseFloat(vNF.textContent) : 0;
    
    // Identifica se é modelo 65 (NFC-e)
    const isNfce = mod ? mod.textContent === '65' : true;
    const isValid = isNfce && valorNum > 0;

    // Calcula Crédito (ex: 2% do valor para válidas)
    const creditoNum = isValid ? (valorNum * 0.02) : 0;

    // Data de emissão
    let dataFormatada = new Date().toLocaleDateString('pt-BR');
    if (dhEmi && dhEmi.textContent) {
        const d = new Date(dhEmi.textContent);
        if (!isNaN(d.getTime())) {
            dataFormatada = d.toLocaleDateString('pt-BR');
        }
    }

    return {
        chave: chave,
        data: dataFormatada,
        valor: valorNum.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }),
        credito: isValid ? creditoNum.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) : '—',
        status: isValid ? 'Válida' : 'Inválida',
        rawValor: valorNum,
        rawCredito: creditoNum,
        isValid: isValid
    };
}

// ==========================================
// ATUALIZAÇÃO DA TABELA E CARDS
// ==========================================
function renderNewData(results) {
    const tbody = document.querySelector('.table-wrapper tbody');
    if (!tbody) return;

    let novasValidas = 0;
    let novoCreditoSum = 0;

    results.forEach(item => {
        totalProcessados++;
        if (item.isValid) {
            novasValidas++;
            novoCreditoSum += item.rawCredito;
        }

        // Adiciona nova linha no topo da tabela
        const tr = document.createElement('tr');
        const badgeClass = item.isValid ? 'status-valid' : 'status-invalid';

        tr.innerHTML = `
            <td class="font-mono">${item.chave}</td>
            <td>${item.data}</td>
            <td>${item.valor}</td>
            <td>${item.credito}</td>
            <td><span class="badge ${badgeClass}">${item.status}</span></td>
        `;

        tbody.insertBefore(tr, tbody.firstChild);
    });

    totalValidas += novasValidas;
    valorTotalCredito += novoCreditoSum;

    // Atualiza os Cards
    const totalCard = document.querySelector('.card-blue .card-value');
    const validCard = document.querySelector('.card-green .card-value');
    const creditCard = document.querySelector('.card-purple .card-value');

    if (totalCard) totalCard.textContent = totalProcessados.toLocaleString('pt-BR');
    if (validCard) validCard.textContent = totalValidas.toLocaleString('pt-BR');
    if (creditCard) creditCard.textContent = valorTotalCredito.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
}