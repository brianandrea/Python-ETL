﻿Vue.component("sm-data-grid-pager", {
    template: `
        <div class="dg-pager bg-light d-flex flex-nowrap">
            <a href="#" class="dg-page dg-page-refresh px-3" @click.prevent="refresh"><i class="fa fa-sync-alt"></i></a>
            
            <template v-if="totalPages > 1">
                <a href="#" class="dg-page dg-page-arrow" @click.prevent="pageTo(1)" :class="{ disabled: !hasPrevPage }"><i class="fa fa-fw fa-angle-double-left"></i></a>
                <a href="#" class="dg-page dg-page-arrow" @click.prevent="pageTo(currentPageIndex - 1)" :class="{ disabled: !hasPrevPage }"><i class="fa fa-fw fa-angle-left"></i></a>
            
                <a v-for="item in pageItems" href="#" @click.prevent="pageTo(item.page)" class="dg-page dg-page-number d-none d-sm-inline" :class="{ active: item.active }">
                    {{ item.label || item.page }}
                </a>
            
                <a href="#" class="dg-page dg-page-arrow" @click.prevent="pageTo(currentPageIndex + 1)" :class="{ disabled: !hasNextPage }"><i class="fa fa-fw fa-angle-right"></i></a>
                <a href="#" class="dg-page dg-page-arrow" @click.prevent="pageTo(totalPages)" :class="{ disabled: !hasNextPage }"><i class="fa fa-fw fa-angle-double-right"></i></a>

                <span class="dg-page text-muted pl-4 text-truncate">{{ currentPageIndex }} / {{ totalPages }}</span>
            </template>

            <span class="dg-page text-muted ml-auto mr-2 text-truncate d-none d-sm-inline">
                <span class="d-none d-md-inline">Anzeigen der Elemente </span>
                <span>{{ firstItemIndex.toLocaleString() }} - {{ lastItemIndex.toLocaleString() }} von {{ total.toLocaleString() }}</span>
            </span>
        </div>
    `,

    props: {
        command: Object,
        rows: Array,
        total: Number,
        maxPagesToDisplay: Number
    },

    created: function () {
    },

    mounted: function () {
    },

    data: function () {
        return {
        }
    },

    computed: {
        currentPageIndex() {
            return this.command.page;
        },

        currentPageSize() {
            return this.command.pageSize;
        },

        totalPages() {
            if (this.currentPageSize === 0)
                return 0;

            var total = this.total / this.currentPageSize;
            if (this.total % this.currentPageSize > 0)
                total++;

            return Math.floor(total);
        },

        hasPrevPage() {
            return this.currentPageIndex > 1;
        },

        hasNextPage() {
            return this.currentPageIndex < this.totalPages;
        },

        isFirstPage() {
            return this.currentPageIndex <= 1;
        },

        isLastPage() {
            return this.currentPageIndex >= this.totalPages;
        },

        firstItemIndex() {
            return ((this.currentPageIndex - 1) * this.currentPageSize) + 1;
        },

        lastItemIndex() {
            return Math.min(this.total, (((this.currentPageIndex - 1) * this.currentPageSize) + this.currentPageSize));
        },

        pageItems() {
            var currentIndex = this.currentPageIndex;
            var totalPages = this.totalPages;
            var maxPages = this.maxPagesToDisplay;
            var start = 1;

            if (currentIndex > maxPages) {
                var v = currentIndex % maxPages;
                start = v === 0 ? currentIndex - maxPages + 1 : currentIndex - v + 1;
            }

            var p = start + maxPages - 1;
            p = Math.min(p, totalPages);

            var items = [];

            if (start > 1) {
                items.push({ page: start - 1, label: '...' });
            }

            for (var i = start; i <= p; i++) {
                items.push({ page: i, label: i.toString(), active: i === currentIndex });
            }

            if (p < totalPages) {
                items.push({ page: p + 1, label: '...' });
            }

            return items;
        }
    },
    methods: {
        refresh() {
            this.$parent.read();
        },

        pageTo(pageIndex) {
            if (pageIndex > 0 && pageIndex <= this.totalPages) {
                this.command.page = pageIndex;
            }
        }
    }
});